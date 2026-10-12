using System.Text.RegularExpressions;
using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Privacy;
using Avizoro.Core.Services;

namespace Avizoro.Core.Tests;

public class PrivacyTests
{
    private readonly CallLog _log = new();

    private (AvizoroService Service, FakeEventStore Store, BreadcrumbBuffer Crumbs) Create(AvizoroOptions options)
    {
        var store = new FakeEventStore(_log);
        var crumbs = new BreadcrumbBuffer(options);
        var service = new AvizoroService(
            store, new FakeUploader(_log), options, new FakeDeviceContextProvider(),
            new FakeRuntimeContextProvider(), crumbs, new FakeLastScreenProvider(null), new FakeScreenshotStore());
        return (service, store, crumbs);
    }

    [Theory]
    [InlineData("Login failed for ana.perez@example.com", "Login failed for [Filtered]")]
    [InlineData("Header: Bearer abc.def-123", "Header: Bearer [Filtered]")]
    [InlineData("card 4111 1111 1111 1111 declined", "card [Filtered] declined")]
    [InlineData("order 1234567890123 shipped", "order 1234567890123 shipped")] // not Luhn-valid
    [InlineData("token eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.sig_nature", "token [Filtered]")]
    [InlineData("IBAN CR05015202001026284066 rejected", "IBAN [Filtered] rejected")]
    public void ScrubText_MasksCommonPii(string input, string expected)
        => Assert.Equal(expected, new PiiScrubber(new AvizoroOptions()).ScrubText(input));

    [Fact]
    public async Task TrackLog_ScrubsMessageDataTagsAndBreadcrumbs()
    {
        var (service, store, _) = Create(new AvizoroOptions());
        service.SetTag("session_id", "abc");
        service.AddBreadcrumb("user typed ana@example.com", data: new Dictionary<string, string> { ["password"] = "hunter2" });

        await service.TrackLogAsync(SeverityLevel.Warning, "retry for bob@example.com",
            new Dictionary<string, string> { ["apiKey"] = "k-123", ["screen"] = "Checkout" });

        var e = Assert.Single(store.Saved);
        Assert.Equal("retry for [Filtered]", e.Message);
        Assert.Equal(PiiScrubber.Filtered, e.Data["apiKey"]);
        Assert.Equal("Checkout", e.Data["screen"]);
        Assert.Equal(PiiScrubber.Filtered, e.Tags["session_id"]);
        var crumb = e.Breadcrumbs.First(b => b.Message.StartsWith("user typed"));
        Assert.Equal("user typed [Filtered]", crumb.Message);
        Assert.Equal(PiiScrubber.Filtered, crumb.Data["password"]);
    }

    [Fact]
    public async Task Scrubbing_CanBeDisabled_AndExtended()
    {
        var (plain, plainStore, _) = Create(new AvizoroOptions { EnablePiiScrubbing = false });
        await plain.TrackLogAsync(SeverityLevel.Information, "mail ana@example.com");
        Assert.Equal("mail ana@example.com", plainStore.Saved.Single().Message);

        var (custom, customStore, _) = Create(new AvizoroOptions
        {
            ScrubbingPatterns = { new Regex(@"CR-\d{6}") },
            ScrubbingSensitiveKeys = { "cedula" }
        });
        await custom.TrackLogAsync(SeverityLevel.Information, "customer CR-123456",
            new Dictionary<string, string> { ["cedula"] = "1-1111-1111" });
        var e = customStore.Saved.Single();
        Assert.Equal("customer [Filtered]", e.Message);
        Assert.Equal(PiiScrubber.Filtered, e.Data["cedula"]);
    }

    [Fact]
    public async Task ExceptionMessages_AreScrubbed_ThroughTheChain()
    {
        var (service, store, _) = Create(new AvizoroOptions());

        await service.TrackExceptionAsync(new InvalidOperationException("outer",
            new ArgumentException("bad email x@y.io")));

        Assert.Equal("bad email [Filtered]", store.Saved.Single().Exception!.InnerException!.Message);
    }

    [Fact]
    public async Task BeforeSend_CanModifyAndDrop()
    {
        var (service, store, _) = Create(new AvizoroOptions
        {
            BeforeSend = e => e.Message == "drop me" ? null : Tag(e)
        });

        await service.TrackLogAsync(SeverityLevel.Information, "drop me");
        await service.TrackLogAsync(SeverityLevel.Information, "keep me");

        var kept = Assert.Single(store.Saved);
        Assert.Equal("keep me", kept.Message);
        Assert.Equal("yes", kept.Tags["seen"]);

        static AvizoroEvent Tag(AvizoroEvent e)
        {
            e.Tags["seen"] = "yes";
            return e;
        }
    }

    [Fact]
    public async Task ThrowingBeforeSend_StillSendsTheEvent()
    {
        var (service, store, _) = Create(new AvizoroOptions { BeforeSend = _ => throw new Exception("bug in hook") });

        await service.TrackLogAsync(SeverityLevel.Error, "important");

        Assert.Single(store.Saved);
    }

    [Fact]
    public async Task BeforeSend_AppliesToCrashes_ButSamplingDoesNot()
    {
        var (service, store, _) = Create(new AvizoroOptions
        {
            SampleRate = 0.0,
            BeforeSend = e => { e.Tags["hooked"] = "1"; return e; }
        });

        await service.TrackLogAsync(SeverityLevel.Error, "sampled out");
        await service.CaptureCrashAsync(new ExceptionInfo { Type = "Fatal", Message = "boom" });

        var crash = Assert.Single(store.Saved);
        Assert.Equal("Fatal", crash.Exception!.Type);
        Assert.Equal("1", crash.Tags["hooked"]);
    }

    [Fact]
    public async Task SampleRate_KeepsRoughlyTheConfiguredShare()
    {
        var (service, store, _) = Create(new AvizoroOptions { SampleRate = 0.25 });

        for (var i = 0; i < 2000; i++)
            await service.TrackLogAsync(SeverityLevel.Information, "n");

        Assert.InRange(store.Saved.Count, 350, 650);
    }

    [Fact]
    public void BeforeBreadcrumb_CanDropAndRewrite()
    {
        var buffer = new BreadcrumbBuffer(new AvizoroOptions
        {
            BeforeBreadcrumb = b => b.Category == "http" ? null : new Breadcrumb { Message = b.Message.ToUpperInvariant() }
        });

        buffer.Add(new Breadcrumb { Category = "http", Message = "GET /" });
        buffer.Add(new Breadcrumb { Message = "tap" });

        Assert.Equal("TAP", Assert.Single(buffer.Snapshot()).Message);
    }
}

public class FeedbackApiTests
{
    [Fact]
    public async Task CaptureFeedback_LinksToLastEvent_AndSkipsScrubbing()
    {
        var log = new CallLog();
        var store = new FakeEventStore(log);
        var service = new AvizoroService(
            store, new FakeUploader(log), new AvizoroOptions { ProjectId = "p" }, new FakeDeviceContextProvider(),
            new FakeRuntimeContextProvider(), new BreadcrumbBuffer(new AvizoroOptions()), new FakeLastScreenProvider(null), new FakeScreenshotStore());

        Assert.Null(service.LastEventId);
        await service.CaptureCrashAsync(new ExceptionInfo { Type = "Boom" });
        var crashId = service.LastEventId!.Value;

        await service.CaptureFeedbackAsync(new UserFeedback { EventId = crashId, Comments = "paid, then it closed", Email = "ana@example.com" });

        var feedback = store.Saved.Last();
        Assert.Equal(EventType.Feedback, feedback.EventType);
        Assert.Equal(crashId, feedback.Feedback!.EventId);
        Assert.Equal("ana@example.com", feedback.Feedback.Email);
        Assert.Equal("p", feedback.ProjectId);
    }

    [Fact]
    public async Task InterfaceDefaults_KeepOtherImplementationsWorking()
    {
        IAvizoro kit = new RecordingKit(); // implements none of the new members
        Assert.Null(kit.LastEventId);
        await kit.CaptureFeedbackAsync(new UserFeedback { EventId = Guid.NewGuid(), Comments = "x" });
    }
}
