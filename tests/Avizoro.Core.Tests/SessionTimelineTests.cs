using System.Net;
using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;

namespace Avizoro.Core.Tests;

internal sealed class FakeSessionTracker : ISessionTracker
{
    public SessionInfo? Current { get; set; } = new() { SessionId = Guid.NewGuid() };
    public Task StartAsync() => Task.CompletedTask;
    public Task PauseAsync() => Task.CompletedTask;
    public Task ResumeAsync() => Task.CompletedTask;
    public Task EndAsync() => Task.CompletedTask;
    public void RecordError() { }
    public void SetUser(string? userId) { }
    public Task CaptureCrashAsync() => Task.CompletedTask;
}

public class SessionTimelineTests
{
    private readonly CallLog _log = new();
    private readonly FakeEventStore _store;
    private readonly FakeUploader _uploader;
    private readonly FakeScreenshotStore _screenshots = new();
    private readonly FakeSessionTracker _sessions = new();

    public SessionTimelineTests()
    {
        _store = new FakeEventStore(_log);
        _uploader = new FakeUploader(_log);
    }

    private static AvizoroOptions Options(Action<AvizoroOptions>? configure = null)
    {
        var options = new AvizoroOptions
        {
            ProjectId = "demo",
            ApplicationName = "DemoApp",
            ApplicationVersion = "1.2.3",
            EnableSessionTimeline = true,
            // Tests flush explicitly; no background timer.
            SessionTimelineFlushInterval = TimeSpan.Zero,
        };
        configure?.Invoke(options);
        return options;
    }

    private SessionTimelineRecorder Recorder(AvizoroOptions? options = null, Func<double>? random = null)
        => new(_store, _uploader, options ?? Options(), new FakeDeviceContextProvider(), _sessions, _screenshots, clock: null, random);

    private static Breadcrumb Crumb(string message, string category = BreadcrumbCategories.Navigation)
        => new() { Message = message, Category = category };

    private IReadOnlyList<AvizoroEvent> Batches => _store.Saved.Where(e => e.EventType == EventType.Timeline).ToList();

    [Fact]
    public async Task Flush_SendsOneBatch_WithSessionSequenceAndContext()
    {
        using var recorder = Recorder();

        recorder.Record(Crumb("HomePage"));
        recorder.Record(Crumb("transfer_clicked", BreadcrumbCategories.User));
        await recorder.FlushAsync();

        var batch = Assert.Single(Batches);
        Assert.Equal(_sessions.Current!.SessionId, batch.SessionId);
        Assert.Equal(0, batch.TimelineSequence);
        Assert.Equal(["HomePage", "transfer_clicked"], batch.Breadcrumbs.Select(b => b.Message));
        Assert.Equal("demo", batch.ProjectId);
        Assert.Equal("Pixel 8", batch.DeviceModel);
        Assert.Equal(["store.save", "uploader.flush"], _log.Entries);
    }

    [Fact]
    public async Task Disabled_RecordsNothing()
    {
        using var recorder = Recorder(Options(o => o.EnableSessionTimeline = false));

        recorder.Record(Crumb("HomePage"));
        await recorder.FlushAsync();

        Assert.Empty(Batches);
    }

    [Fact]
    public async Task WithoutSession_RecordsNothing()
    {
        _sessions.Current = null;
        using var recorder = Recorder();

        recorder.Record(Crumb("HomePage"));
        await recorder.FlushAsync();

        Assert.Empty(Batches);
    }

    [Fact]
    public async Task NewSession_ClosesPreviousBatch_AndRestartsSequence()
    {
        using var recorder = Recorder();
        var first = _sessions.Current!.SessionId;
        recorder.Record(Crumb("A"));
        recorder.Record(Crumb("B"));

        _sessions.Current = new SessionInfo { SessionId = Guid.NewGuid() };
        recorder.Record(Crumb("C"));
        await Task.Yield();
        await recorder.FlushAsync();

        Assert.Equal(2, Batches.Count);
        Assert.Equal(first, Batches[0].SessionId);
        Assert.Equal(["A", "B"], Batches[0].Breadcrumbs.Select(b => b.Message));
        Assert.Equal(_sessions.Current.SessionId, Batches[1].SessionId);
        Assert.Equal(0, Batches[1].TimelineSequence);
    }

    [Fact]
    public async Task FullBatch_IsCutAutomatically_WithContiguousSequences()
    {
        using var recorder = Recorder();

        for (var i = 0; i < SessionTimelineRecorder.BatchSize + 3; i++)
            recorder.Record(Crumb($"step {i}"));
        await recorder.FlushAsync();

        Assert.Equal(2, Batches.Count);
        Assert.Equal(SessionTimelineRecorder.BatchSize, Batches[0].Breadcrumbs.Count);
        Assert.Equal(SessionTimelineRecorder.BatchSize, Batches[1].TimelineSequence);
        Assert.Equal(3, Batches[1].Breadcrumbs.Count);
    }

    [Fact]
    public async Task EntryLimit_StopsRecording_WithASingleMarker()
    {
        using var recorder = Recorder(Options(o => o.SessionTimelineMaxEntries = 2));

        for (var i = 0; i < 5; i++)
            recorder.Record(Crumb($"step {i}"));
        await recorder.FlushAsync();

        var entries = Batches.SelectMany(b => b.Breadcrumbs).ToList();
        Assert.Equal(3, entries.Count);
        Assert.Equal(BreadcrumbCategories.System, entries[2].Category);
        Assert.Equal(SeverityLevel.Warning, entries[2].Level);
    }

    [Fact]
    public async Task SampledOutSession_RecordsNothing()
    {
        using var recorder = Recorder(Options(o => o.SessionTimelineSampleRate = 0.5), random: () => 0.9);

        recorder.Record(Crumb("HomePage"));
        await recorder.FlushAsync();

        Assert.Empty(Batches);
    }

    [Fact]
    public async Task Screenshot_ClosesBatch_AndIsStoredUnderTheBatchId_BeforeTheEvent()
    {
        using var recorder = Recorder();
        recorder.Record(Crumb("TransferPage"));
        var image = new byte[] { 0xFF, 0xD8, 0xFF, 1, 2, 3 };

        var recorded = await recorder.RecordScreenshotAsync(image, "TransferPage");

        Assert.True(recorded);
        var batch = Assert.Single(Batches);
        var shot = batch.Breadcrumbs.Last();
        Assert.Equal(BreadcrumbCategories.Screenshot, shot.Category);
        Assert.Equal("TransferPage", shot.Data["screen"]);
        Assert.Equal(image, _screenshots.Saved[batch.Id]);
    }

    [Fact]
    public async Task Screenshot_InASampledOutSession_StillPersistsThePreviousSessionsSteps()
    {
        var draws = new Queue<double>([0.1, 0.9]); // first session recorded, second sampled out
        using var recorder = Recorder(Options(o => o.SessionTimelineSampleRate = 0.5), random: () => draws.Dequeue());
        var first = _sessions.Current!.SessionId;
        recorder.Record(Crumb("HomePage"));

        _sessions.Current = new SessionInfo { SessionId = Guid.NewGuid() };
        var recorded = await recorder.RecordScreenshotAsync([1, 2, 3]);

        Assert.False(recorded);
        var batch = Assert.Single(Batches);
        Assert.Equal(first, batch.SessionId);
        Assert.Empty(_screenshots.Saved);
    }

    [Fact]
    public async Task Screenshot_WithoutSession_IsNotRecorded()
    {
        _sessions.Current = null;
        using var recorder = Recorder();

        Assert.False(await recorder.RecordScreenshotAsync([1, 2, 3]));
        Assert.Empty(_screenshots.Saved);
    }

    [Fact]
    public async Task Batches_AreScrubbed_WithoutAlteringTheTrail()
    {
        var options = Options();
        using var recorder = Recorder(options);
        var buffer = new BreadcrumbBuffer(options, recorder);

        buffer.Add(new Breadcrumb { Message = "Signed in as ana@example.com", Data = { ["token"] = "abc123" } });
        await recorder.FlushAsync();

        var entry = Batches.Single().Breadcrumbs.Single();
        Assert.DoesNotContain("ana@example.com", entry.Message);
        Assert.Equal("[Filtered]", entry.Data["token"]);
        Assert.Contains("ana@example.com", buffer.Snapshot().Single().Message);
    }

    [Fact]
    public async Task BreadcrumbBuffer_ForwardsOnlyBreadcrumbsKeptByTheHook()
    {
        var options = Options(o => o.BeforeBreadcrumb = b => b.Message.StartsWith("secret") ? null : b);
        using var recorder = Recorder(options);
        var buffer = new BreadcrumbBuffer(options, recorder);

        buffer.Add(Crumb("HomePage"));
        buffer.Add(Crumb("secret page"));
        await recorder.FlushAsync();

        Assert.Equal(["HomePage"], Batches.Single().Breadcrumbs.Select(b => b.Message));
    }

    [Fact]
    public async Task Service_StampsSessionId_AndCrashPersistsTheTimelineWithoutUploading()
    {
        var options = Options();
        using var recorder = Recorder(options);
        var buffer = new BreadcrumbBuffer(options, recorder);
        var service = new AvizoroService(
            _store, _uploader, options, new FakeDeviceContextProvider(), new FakeRuntimeContextProvider(),
            buffer, new FakeLastScreenProvider(null), _screenshots, _sessions, recorder);

        service.TrackAction("pay_clicked");
        await service.CaptureCrashAsync(ExceptionInfo.FromException(new InvalidOperationException("boom")));

        var crash = _store.Saved.Single(e => e.EventType == EventType.Exception);
        Assert.Equal(_sessions.Current!.SessionId, crash.SessionId);
        Assert.Equal(["pay_clicked"], Batches.Single().Breadcrumbs.Select(b => b.Message));
        Assert.Equal(0, _uploader.FlushCount);
    }

    [Fact]
    public void Extensions_UseTheWellKnownCategories()
    {
        var options = Options(o => o.EnableSessionTimeline = false);
        var buffer = new BreadcrumbBuffer(options);
        var service = new AvizoroService(
            _store, _uploader, options, new FakeDeviceContextProvider(), new FakeRuntimeContextProvider(),
            buffer, new FakeLastScreenProvider(null), _screenshots);

        service.TrackAction("transfer_clicked", new Dictionary<string, string> { ["amount"] = "250" });
        service.TrackScreen("SettingsTab");

        var crumbs = buffer.Snapshot();
        Assert.Equal(BreadcrumbCategories.User, crumbs[0].Category);
        Assert.Equal("250", crumbs[0].Data["amount"]);
        Assert.Equal(BreadcrumbCategories.Navigation, crumbs[1].Category);
    }

    [Fact]
    public async Task HttpHandler_AddsBodySizesFromContentLength()
    {
        var options = Options(o => o.EnableSessionTimeline = false);
        var buffer = new BreadcrumbBuffer(options);
        var service = new AvizoroService(
            _store, _uploader, options, new FakeDeviceContextProvider(), new FakeRuntimeContextProvider(),
            buffer, new FakeLastScreenProvider(null), _screenshots);
        using var client = new HttpClient(new AvizoroHttpHandler(service) { InnerHandler = new SizedHandler() });

        await client.PostAsync("https://api.example.com/transfer", new StringContent("{\"amount\":250}"));

        var data = buffer.Snapshot().Single().Data;
        Assert.Equal("14", data["request_size"]);
        Assert.Equal("5", data["response_size"]);
    }

    private sealed class SizedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("hello") });
    }
}
