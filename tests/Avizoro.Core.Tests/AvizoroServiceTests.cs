using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;

namespace Avizoro.Core.Tests;

public class AvizoroServiceTests
{
    private sealed class Harness
    {
        public CallLog Log { get; } = new();
        public FakeEventStore Store { get; }
        public FakeUploader Uploader { get; }
        public FakeScreenshotStore Screenshots { get; } = new();
        public BreadcrumbBuffer Breadcrumbs { get; }
        public AvizoroOptions Options { get; }
        public AvizoroService Service { get; }

        public Harness(
            AvizoroOptions? options = null,
            byte[]? lastScreen = null,
            Func<DeviceRuntime>? runtime = null)
        {
            Options = options ?? new AvizoroOptions
            {
                ProjectId = "demo",
                ApplicationName = "DemoApp",
                ApplicationVersion = "1.2.3",
                Environment = "staging"
            };
            Store = new FakeEventStore(Log);
            Uploader = new FakeUploader(Log);
            Breadcrumbs = new BreadcrumbBuffer(Options);
            Service = new AvizoroService(
                Store,
                Uploader,
                Options,
                new FakeDeviceContextProvider(),
                new FakeRuntimeContextProvider(runtime),
                Breadcrumbs,
                new FakeLastScreenProvider(lastScreen),
                Screenshots);
        }
    }

    [Fact]
    public async Task TrackAsync_Enriches_WithAppDeviceAndRuntimeContext()
    {
        var h = new Harness();

        await h.Service.TrackAsync(new AvizoroEvent { EventType = EventType.Log, Message = "hi" });

        var saved = Assert.Single(h.Store.Saved);
        Assert.Equal("demo", saved.ProjectId);
        Assert.Equal("DemoApp", saved.ApplicationName);
        Assert.Equal("1.2.3", saved.ApplicationVersion);
        Assert.Equal("staging", saved.Environment);
        Assert.Equal("Android", saved.Platform);
        Assert.Equal("Google", saved.Manufacturer);
        Assert.NotNull(saved.DeviceRuntime);
    }

    [Fact]
    public async Task TrackAsync_Release_DefaultsToApplicationVersion_WhenUnset()
    {
        var h = new Harness();

        await h.Service.TrackAsync(new AvizoroEvent());

        Assert.Equal("1.2.3", Assert.Single(h.Store.Saved).Release);
    }

    [Fact]
    public async Task TrackAsync_Release_UsesExplicitReleaseWhenSet()
    {
        var h = new Harness(new AvizoroOptions
        {
            ProjectId = "demo",
            ApplicationVersion = "1.2.3",
            Release = "canary-42"
        });

        await h.Service.TrackAsync(new AvizoroEvent());

        Assert.Equal("canary-42", Assert.Single(h.Store.Saved).Release);
    }

    [Fact]
    public async Task TrackAsync_PersistsBeforeFlushing()
    {
        var h = new Harness();

        await h.Service.TrackAsync(new AvizoroEvent());

        // Store-and-forward guarantee: durable write happens before the network drain.
        Assert.Equal(new[] { "store.save", "uploader.flush" }, h.Log.Entries);
    }

    [Fact]
    public async Task TrackAsync_AttachesCurrentScopeTagsAndUser()
    {
        var h = new Harness();
        h.Service.SetUser("anon-7");
        h.Service.SetTag("plan", "premium");

        await h.Service.TrackAsync(new AvizoroEvent());

        var saved = Assert.Single(h.Store.Saved);
        Assert.Equal("anon-7", saved.UserId);
        Assert.Equal("premium", saved.Tags["plan"]);
    }

    [Fact]
    public async Task TrackAsync_AttachesBreadcrumbSnapshot_WhenEventHasNone()
    {
        var h = new Harness();
        h.Service.AddBreadcrumb("tapped checkout");

        await h.Service.TrackAsync(new AvizoroEvent());

        var saved = Assert.Single(h.Store.Saved);
        Assert.Equal("tapped checkout", Assert.Single(saved.Breadcrumbs).Message);
    }

    [Fact]
    public async Task SetTag_WithNullValue_RemovesTheTag()
    {
        var h = new Harness();
        h.Service.SetTag("plan", "premium");
        h.Service.SetTag("plan", null);

        // Prove removal via a subsequent tracked event.
        await h.Service.TrackAsync(new AvizoroEvent());

        Assert.False(Assert.Single(h.Store.Saved).Tags.ContainsKey("plan"));
    }

    [Fact]
    public async Task TrackExceptionAsync_FromManagedException_BuildsErrorLevelExceptionEvent()
    {
        var h = new Harness();

        await h.Service.TrackExceptionAsync(new InvalidOperationException("boom"));

        var saved = Assert.Single(h.Store.Saved);
        Assert.Equal(EventType.Exception, saved.EventType);
        Assert.Equal(SeverityLevel.Error, saved.Level);
        Assert.NotNull(saved.Exception);
        Assert.Equal("System.InvalidOperationException", saved.Exception!.Type);
        Assert.Equal("boom", saved.Exception.Message);
    }

    [Fact]
    public async Task TrackLogAsync_CreatesLogEvent_AndDropsBreadcrumb()
    {
        var h = new Harness();

        await h.Service.TrackLogAsync(
            SeverityLevel.Warning, "cart mismatch",
            new Dictionary<string, string> { ["sku"] = "A1" });

        var saved = Assert.Single(h.Store.Saved);
        Assert.Equal(EventType.Log, saved.EventType);
        Assert.Equal(SeverityLevel.Warning, saved.Level);
        Assert.Equal("cart mismatch", saved.Message);
        Assert.Equal("A1", saved.Data["sku"]);

        // The log doubles as a breadcrumb for whatever comes next.
        Assert.Contains(h.Breadcrumbs.Snapshot(), c => c.Message == "cart mismatch");
    }

    [Fact]
    public async Task CaptureCrashAsync_PersistsButDoesNotFlush()
    {
        var h = new Harness();

        await h.Service.CaptureCrashAsync(new ExceptionInfo { Type = "System.Exception", Message = "fatal" });

        // Fatal path: the dying process must not attempt a network call.
        Assert.Single(h.Store.Saved);
        Assert.Equal(0, h.Uploader.FlushCount);
        Assert.DoesNotContain("uploader.flush", h.Log.Entries);
    }

    [Fact]
    public async Task Enrich_RuntimeCaptureFailure_IsSwallowed_EventStillSaved()
    {
        var h = new Harness(runtime: () => throw new InvalidOperationException("sensor blew up"));

        await h.Service.TrackAsync(new AvizoroEvent());

        var saved = Assert.Single(h.Store.Saved);
        Assert.Null(saved.DeviceRuntime);
    }

    [Fact]
    public async Task Screenshot_NotAttached_WhenCaptureDisabled()
    {
        var h = new Harness(
            options: new AvizoroOptions { ProjectId = "demo", EnableScreenCapture = false },
            lastScreen: new byte[] { 1, 2, 3 });

        await h.Service.TrackExceptionAsync(new ExceptionInfo { Type = "System.Exception", Message = "x" });

        Assert.Empty(h.Screenshots.Saved);
    }

    [Fact]
    public async Task Screenshot_Attached_WhenCaptureEnabledAndFrameAvailable()
    {
        var h = new Harness(
            options: new AvizoroOptions { ProjectId = "demo", EnableScreenCapture = true },
            lastScreen: new byte[] { 1, 2, 3 });

        await h.Service.TrackExceptionAsync(new ExceptionInfo { Type = "System.Exception", Message = "x" });

        var saved = Assert.Single(h.Store.Saved);
        Assert.True(h.Screenshots.Saved.ContainsKey(saved.Id));
        Assert.Equal(new byte[] { 1, 2, 3 }, h.Screenshots.Saved[saved.Id]);
    }

    [Fact]
    public async Task Screenshot_NotAttached_WhenEnabledButNoFrameCaptured()
    {
        var h = new Harness(
            options: new AvizoroOptions { ProjectId = "demo", EnableScreenCapture = true },
            lastScreen: null);

        await h.Service.TrackExceptionAsync(new ExceptionInfo { Type = "System.Exception", Message = "x" });

        Assert.Empty(h.Screenshots.Saved);
    }
}
