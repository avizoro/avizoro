using Avizoro.Core.Configuration;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Providers;
using Avizoro.Core.Services;

namespace Avizoro.Core.Tests;

internal sealed class ManualClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

internal sealed class FixedInstallationId : IInstallationIdProvider
{
    public string GetInstallationId() => "install-1";
}

public class SessionTrackerTests
{
    private readonly CallLog _log = new();
    private readonly FakeEventStore _store;
    private readonly FakeUploader _uploader;
    private readonly ManualClock _clock = new();
    private readonly SessionTracker _tracker;

    public SessionTrackerTests()
    {
        _store = new FakeEventStore(_log);
        _uploader = new FakeUploader(_log);
        _tracker = new SessionTracker(
            _store, _uploader,
            new AvizoroOptions { ProjectId = "p", ApplicationVersion = "1.2.0", SessionTimeout = TimeSpan.FromSeconds(30) },
            new FakeDeviceContextProvider(), new FixedInstallationId(), _clock);
    }

    private IEnumerable<SessionInfo> Updates => _store.Saved.Select(e => e.Session!);

    [Fact]
    public async Task Start_SendsInitUpdate_WithContext()
    {
        await _tracker.StartAsync();

        var e = Assert.Single(_store.Saved);
        Assert.Equal(EventType.Session, e.EventType);
        Assert.Equal("p", e.ProjectId);
        Assert.Equal("1.2.0", e.Release);
        Assert.True(e.Session!.Init);
        Assert.Equal(SessionStatus.Ok, e.Session.Status);
        Assert.Equal("install-1", e.Session.DistinctId);
        Assert.Equal(1, _uploader.FlushCount);
    }

    [Fact]
    public async Task Start_IsIdempotentWhileRunning()
    {
        await _tracker.StartAsync();
        await _tracker.StartAsync();

        Assert.Single(_store.Saved);
    }

    [Fact]
    public async Task PauseAndQuickResume_ContinuesSameSession()
    {
        await _tracker.StartAsync();
        _clock.Advance(TimeSpan.FromSeconds(10));
        await _tracker.PauseAsync();
        _clock.Advance(TimeSpan.FromSeconds(5));
        await _tracker.ResumeAsync();

        var updates = Updates.ToList();
        Assert.Equal(3, updates.Count);
        Assert.Single(updates.Select(u => u.SessionId).Distinct());
        Assert.Equal(SessionStatus.Exited, updates[1].Status);
        Assert.Equal(10, updates[1].DurationSeconds);
        Assert.Equal(SessionStatus.Ok, updates[2].Status);
        Assert.Equal([1L, 2L, 3L], updates.Select(u => u.Sequence));
    }

    [Fact]
    public async Task ResumeAfterTimeout_StartsNewSession()
    {
        await _tracker.StartAsync();
        await _tracker.PauseAsync();
        _clock.Advance(TimeSpan.FromMinutes(5));
        await _tracker.ResumeAsync();

        var updates = Updates.ToList();
        Assert.Equal(2, updates.Select(u => u.SessionId).Distinct().Count());
        Assert.True(updates[^1].Init);
    }

    [Fact]
    public async Task Crash_MarksCrashed_PersistsWithoutFlush_AndCarriesErrors()
    {
        await _tracker.StartAsync();
        _tracker.RecordError();
        _tracker.RecordError();
        var flushesBefore = _uploader.FlushCount;

        await _tracker.CaptureCrashAsync();

        var last = Updates.Last();
        Assert.Equal(SessionStatus.Crashed, last.Status);
        Assert.Equal(2, last.Errors);
        Assert.Equal(flushesBefore, _uploader.FlushCount);
        Assert.Null(_tracker.Current);
    }

    [Fact]
    public async Task End_SendsExited_AndClearsCurrent()
    {
        await _tracker.StartAsync();
        _clock.Advance(TimeSpan.FromSeconds(42));
        await _tracker.EndAsync();

        var last = Updates.Last();
        Assert.Equal(SessionStatus.Exited, last.Status);
        Assert.Equal(42, last.DurationSeconds);
        Assert.Null(_tracker.Current);
    }

    [Fact]
    public async Task SetUser_IsAttachedToLaterUpdates()
    {
        _tracker.SetUser("user-9");
        await _tracker.StartAsync();

        Assert.Equal("user-9", _store.Saved.Single().UserId);
    }

    [Fact]
    public async Task Service_CountsHandledErrors_AndCrashClosesSession()
    {
        var service = new AvizoroService(
            _store, _uploader, new AvizoroOptions(), new FakeDeviceContextProvider(),
            new FakeRuntimeContextProvider(), new Avizoro.Core.Diagnostics.BreadcrumbBuffer(new AvizoroOptions()),
            new FakeLastScreenProvider(null), new FakeScreenshotStore(), _tracker);

        await _tracker.StartAsync();
        await service.TrackExceptionAsync(new InvalidOperationException("handled"));
        await service.CaptureCrashAsync(new ExceptionInfo { Type = "Boom" });

        var last = Updates.Last();
        Assert.Equal(SessionStatus.Crashed, last.Status);
        Assert.Equal(1, last.Errors);
    }
}
