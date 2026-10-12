using Avizoro.Core.Configuration;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Privacy;
using Avizoro.Core.Providers;
using Avizoro.Core.Storage;

namespace Avizoro.Core.Services;

/// <summary>
/// Default <see cref="ISessionTimeline"/>. Entries are buffered briefly in memory and written as
/// <see cref="EventType.Timeline"/> batches to the durable <see cref="IEventStore"/>, which the
/// uploader drains like any other event. A batch is cut when the session changes, every
/// <see cref="BatchSize"/> entries, after a screenshot, on each
/// <see cref="AvizoroOptions.SessionTimelineFlushInterval"/>, and on flush (background, crash).
/// At most one flush interval of entries can be lost if the process is killed without a crash handler.
/// </summary>
public sealed class SessionTimelineRecorder : ISessionTimeline, IDisposable
{
    /// <summary>Entries per timeline batch.</summary>
    public const int BatchSize = 50;

    private readonly IEventStore _store;
    private readonly IEventUploader _uploader;
    private readonly AvizoroOptions _options;
    private readonly IDeviceContextProvider _device;
    private readonly ISessionTracker _sessions;
    private readonly IScreenshotStore _screenshots;
    private readonly TimeProvider _clock;
    private readonly Func<double> _random;
    private readonly PiiScrubber? _scrubber;
    private readonly object _gate = new();

    private readonly List<Breadcrumb> _pending = new();
    private Guid _sessionId;
    private bool _sampledIn;
    private long _nextSequence;
    private int _recorded;
    private bool _truncated;
    private ITimer? _timer;
    private bool _disposed;

    public SessionTimelineRecorder(
        IEventStore store,
        IEventUploader uploader,
        AvizoroOptions options,
        IDeviceContextProvider device,
        ISessionTracker sessions,
        IScreenshotStore screenshots)
        : this(store, uploader, options, device, sessions, screenshots, clock: null, random: null)
    {
    }

    /// <summary>Test seam: a controllable clock and sampling source.</summary>
    public SessionTimelineRecorder(
        IEventStore store,
        IEventUploader uploader,
        AvizoroOptions options,
        IDeviceContextProvider device,
        ISessionTracker sessions,
        IScreenshotStore screenshots,
        TimeProvider? clock,
        Func<double>? random)
    {
        _store = store;
        _uploader = uploader;
        _options = options;
        _device = device;
        _sessions = sessions;
        _screenshots = screenshots;
        _clock = clock ?? TimeProvider.System;
        _random = random ?? Random.Shared.NextDouble;
        _scrubber = options.EnablePiiScrubbing ? new PiiScrubber(options) : null;
    }

    public void Record(Breadcrumb breadcrumb)
    {
        if (!_options.EnableSessionTimeline)
            return;

        var session = CurrentSessionId();
        if (session is null)
            return;

        var batches = new List<Batch>(2);
        lock (_gate)
        {
            if (_disposed)
                return;

            SwitchSessionLocked(session.Value, batches);
            if (TryAddLocked(Copy(breadcrumb)) && _pending.Count >= BatchSize)
                AddBatchLocked(batches, image: null);
        }

        EnsureTimer();
        foreach (var batch in batches)
            _ = PersistAsync(batch, upload: true);
    }

    public async Task<bool> RecordScreenshotAsync(byte[] image, string? screen = null)
    {
        if (!_options.EnableSessionTimeline || image is not { Length: > 0 })
            return false;

        var session = CurrentSessionId();
        if (session is null)
            return false;

        var entry = new Breadcrumb
        {
            Timestamp = Now,
            Category = BreadcrumbCategories.Screenshot,
            Message = "Screenshot",
            Level = SeverityLevel.Information,
        };
        if (!string.IsNullOrEmpty(screen))
            entry.Data["screen"] = screen;

        var batches = new List<Batch>(2);
        var recorded = false;
        lock (_gate)
        {
            if (_disposed)
                return false;

            // May close the previous session's batch: that one is persisted below either way.
            SwitchSessionLocked(session.Value, batches);
            if (TryAddLocked(entry))
            {
                // The server keys a screenshot by its batch, so a screenshot always closes its batch.
                AddBatchLocked(batches, image);
                recorded = true;
            }
        }

        foreach (var batch in batches)
            await PersistAsync(batch, upload: true).ConfigureAwait(false);
        return recorded;
    }

    public async Task FlushAsync(bool upload = true)
    {
        Batch? batch = null;
        lock (_gate)
        {
            if (_pending.Count > 0)
                batch = TakeBatchLocked(image: null);
        }

        if (batch is not null)
            await PersistAsync(batch, upload).ConfigureAwait(false);
        else if (upload)
            await SafeFlushUploaderAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private Guid? CurrentSessionId()
    {
        try
        {
            return _sessions.Current?.SessionId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>On a new session, closes the previous session's batch and decides sampling. Caller holds the gate.</summary>
    private void SwitchSessionLocked(Guid sessionId, List<Batch> batches)
    {
        if (sessionId == _sessionId)
            return;

        if (_pending.Count > 0)
            AddBatchLocked(batches, image: null);

        _sessionId = sessionId;
        _nextSequence = 0;
        _recorded = 0;
        _truncated = false;

        var rate = _options.SessionTimelineSampleRate;
        _sampledIn = rate >= 1.0 || (rate > 0.0 && _random() < rate);
    }

    /// <summary>Adds an entry unless the session is sampled out or full. Caller holds the gate.</summary>
    private bool TryAddLocked(Breadcrumb entry)
    {
        if (!_sampledIn)
            return false;

        if (_recorded >= Math.Max(0, _options.SessionTimelineMaxEntries))
        {
            if (!_truncated)
            {
                // One marker so the timeline says why it stops, instead of silently ending.
                _truncated = true;
                _pending.Add(new Breadcrumb
                {
                    Timestamp = Now,
                    Category = BreadcrumbCategories.System,
                    Message = "Timeline limit reached; later activity was not recorded.",
                    Level = SeverityLevel.Warning,
                    Data = { ["limit"] = _options.SessionTimelineMaxEntries.ToString() },
                });
                _nextSequence++;
            }
            return false;
        }

        _pending.Add(entry);
        _nextSequence++;
        _recorded++;
        return true;
    }

    private void AddBatchLocked(List<Batch> batches, byte[]? image) => batches.Add(TakeBatchLocked(image));

    private Batch TakeBatchLocked(byte[]? image)
    {
        var entries = _pending.ToList();
        _pending.Clear();
        return new Batch(_sessionId, _nextSequence - entries.Count, entries, image);
    }

    private void EnsureTimer()
    {
        if (_timer is not null)
            return;

        lock (_gate)
        {
            if (_timer is not null || _disposed)
                return;

            var interval = _options.SessionTimelineFlushInterval;
            if (interval <= TimeSpan.Zero)
                return;

            _timer = _clock.CreateTimer(_ => _ = FlushAsync(upload: true), null, interval, interval);
        }
    }

    private async Task PersistAsync(Batch batch, bool upload)
    {
        // Timeline recording is best-effort: it must never break the host app.
        try
        {
            if (batch.Entries.Count == 0)
                return;

            var e = new AvizoroEvent
            {
                EventType = EventType.Timeline,
                SessionId = batch.SessionId,
                TimelineSequence = batch.StartSequence,
                Breadcrumbs = batch.Entries,
                Timestamp = Now,
            };
            EventContext.Stamp(e, _options, _device);
            _scrubber?.Scrub(e);

            // The image must be in its store before the event can be delivered: the uploader
            // ships an event's screenshot right after the event itself.
            if (batch.Image is { Length: > 0 })
                await _screenshots.SaveAsync(e.Id, batch.Image).ConfigureAwait(false);

            await _store.SaveAsync(e).ConfigureAwait(false);

            if (upload)
                await SafeFlushUploaderAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task SafeFlushUploaderAsync()
    {
        try
        {
            await _uploader.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
        }
    }

    /// <summary>The trail and the timeline hold separate copies: scrubbing one must not alter the other.</summary>
    private static Breadcrumb Copy(Breadcrumb b) => new()
    {
        Timestamp = b.Timestamp,
        Category = b.Category,
        Message = b.Message,
        Level = b.Level,
        Data = new Dictionary<string, string>(b.Data),
    };

    private sealed record Batch(Guid SessionId, long StartSequence, List<Breadcrumb> Entries, byte[]? Image);
}
