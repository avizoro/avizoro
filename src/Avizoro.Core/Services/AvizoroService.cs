using System.Collections.Concurrent;
using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Privacy;
using Avizoro.Core.Providers;
using Avizoro.Core.Storage;

namespace Avizoro.Core.Services;

public class AvizoroService : IAvizoro
{
    private readonly IEventStore _store;
    private readonly IEventUploader _uploader;
    private readonly AvizoroOptions _options;
    private readonly IDeviceContextProvider _device;
    private readonly IRuntimeContextProvider _runtime;
    private readonly IBreadcrumbBuffer _breadcrumbs;
    private readonly ILastScreenProvider _lastScreen;
    private readonly IScreenshotStore _screenshots;
    private readonly ISessionTracker? _sessions;
    private readonly ISessionTimeline? _timeline;
    private readonly PiiScrubber? _scrubber;

    // Mutable per-session scope. This service is a singleton, so these carry
    // across events until changed.
    private volatile string? _userId;
    private Guid? _lastEventId;
    private readonly ConcurrentDictionary<string, string> _tags = new();

    public AvizoroService(
        IEventStore store,
        IEventUploader uploader,
        AvizoroOptions options,
        IDeviceContextProvider device,
        IRuntimeContextProvider runtime,
        IBreadcrumbBuffer breadcrumbs,
        ILastScreenProvider lastScreen,
        IScreenshotStore screenshots)
        : this(store, uploader, options, device, runtime, breadcrumbs, lastScreen, screenshots, sessions: null)
    {
    }

    /// <summary>
    /// Full constructor. <paramref name="sessions"/> is optional: when supplied,
    /// handled errors are counted against the current session and a fatal crash
    /// marks it crashed (release health).
    /// </summary>
    public AvizoroService(
        IEventStore store,
        IEventUploader uploader,
        AvizoroOptions options,
        IDeviceContextProvider device,
        IRuntimeContextProvider runtime,
        IBreadcrumbBuffer breadcrumbs,
        ILastScreenProvider lastScreen,
        IScreenshotStore screenshots,
        ISessionTracker? sessions)
        : this(store, uploader, options, device, runtime, breadcrumbs, lastScreen, screenshots, sessions, timeline: null)
    {
    }

    /// <summary>
    /// Full constructor with the session timeline. <paramref name="timeline"/> is optional:
    /// when supplied, pending timeline entries are persisted together with a crash.
    /// </summary>
    public AvizoroService(
        IEventStore store,
        IEventUploader uploader,
        AvizoroOptions options,
        IDeviceContextProvider device,
        IRuntimeContextProvider runtime,
        IBreadcrumbBuffer breadcrumbs,
        ILastScreenProvider lastScreen,
        IScreenshotStore screenshots,
        ISessionTracker? sessions,
        ISessionTimeline? timeline)
    {
        _sessions = sessions;
        _timeline = timeline;
        _scrubber = options.EnablePiiScrubbing ? new PiiScrubber(options) : null;
        _store = store;
        _uploader = uploader;
        _options = options;
        _device = device;
        _runtime = runtime;
        _breadcrumbs = breadcrumbs;
        _lastScreen = lastScreen;
        _screenshots = screenshots;
    }

    public async Task TrackAsync(AvizoroEvent avizoroEvent)
    {
        if (!Sampled())
            return;

        Enrich(avizoroEvent);
        var processed = Process(avizoroEvent);
        if (processed is null)
            return; // dropped by BeforeSend

        // Store-and-forward: persist first (durable even if the app dies now),
        // then opportunistically drain the queue to the server.
        await _store.SaveAsync(processed);
        _lastEventId = processed.Id;
        await _uploader.FlushAsync();
    }

    public Guid? LastEventId => _lastEventId;

    public async Task CaptureFeedbackAsync(UserFeedback feedback)
    {
        if (feedback.EventId == Guid.Empty || string.IsNullOrWhiteSpace(feedback.Comments))
            return;

        // Written by the user on purpose: no sampling, hooks or scrubbing.
        var e = new AvizoroEvent { EventType = EventType.Feedback, Feedback = feedback };
        EventContext.Stamp(e, _options, _device);
        e.UserId = AvizoroScope.Current?.UserId ?? _userId;

        await _store.SaveAsync(e);
        await _uploader.FlushAsync();
    }

    public Task TrackExceptionAsync(Exception exception)
    {
        ExceptionCapture.MarkCaptured(exception);
        return TrackExceptionAsync(ExceptionInfo.FromException(exception));
    }

    public async Task TrackExceptionAsync(ExceptionInfo exception)
    {
        _sessions?.RecordError();

        var exceptionEvent = BuildExceptionEvent(exception);
        if (!Sampled())
            return;

        Enrich(exceptionEvent);
        var processed = Process(exceptionEvent);
        if (processed is null)
            return;

        // Only attach the screenshot for events that will actually be sent.
        await AttachScreenshotAsync(processed);
        await _store.SaveAsync(processed);
        _lastEventId = processed.Id;
        await _uploader.FlushAsync();
    }

    public Task TrackLogAsync(SeverityLevel level, string message, IDictionary<string, string>? data = null)
    {
        // A captured log is also a breadcrumb for whatever comes after it.
        AddBreadcrumb(message, BreadcrumbCategories.Log, level, data);

        var logEvent = new AvizoroEvent
        {
            EventType = EventType.Log,
            Level = level,
            Message = message
        };

        if (data is not null)
        {
            foreach (var kv in data)
                logEvent.Data[kv.Key] = kv.Value;
        }

        return TrackAsync(logEvent);
    }

    public void AddBreadcrumb(string message, string category = BreadcrumbCategories.Custom,
        SeverityLevel level = SeverityLevel.Information, IDictionary<string, string>? data = null)
    {
        var breadcrumb = new Breadcrumb
        {
            Message = message,
            Category = category,
            Level = level,
            Data = data is null ? new() : new Dictionary<string, string>(data)
        };

        // Inside a scope (e.g. one request), the trail belongs to that scope.
        if (AvizoroScope.Current is { } scope)
        {
            if (_options.BeforeBreadcrumb is { } hook)
            {
                try
                {
                    breadcrumb = hook(breadcrumb);
                }
                catch
                {
                }
                if (breadcrumb is null)
                    return;
            }
            scope.AddBreadcrumb(breadcrumb);
            return;
        }

        _breadcrumbs.Add(breadcrumb);
    }

    public void SetUser(string? userId)
    {
        _userId = userId;
        _sessions?.SetUser(userId);
    }

    public void SetTag(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (value is null)
            _tags.TryRemove(key, out _);
        else
            _tags[key] = value;
    }

    public async Task CaptureCrashAsync(ExceptionInfo exception)
    {
        var avizoroEvent = BuildExceptionEvent(exception);
        Enrich(avizoroEvent);

        // Crashes are never sampled out, but BeforeSend/scrubbing still apply.
        var processed = Process(avizoroEvent);
        if (processed is null)
        {
            await PersistTimelineAsync();
            await CloseCrashedSessionAsync();
            return;
        }
        avizoroEvent = processed;

        _lastEventId = avizoroEvent.Id;

        // Persist only — no flush. The process is dying; the uploader picks this
        // up on the next launch. Both writes are fast local writes; the screenshot
        // bytes were already captured proactively (never on this dying thread).
        await _store.SaveAsync(avizoroEvent);
        await AttachScreenshotAsync(avizoroEvent);

        // Keep the timeline leading up to the crash (persist-only, like the crash itself).
        await PersistTimelineAsync();

        // Close the session as crashed (also persist-only), after the crash itself
        // is safely stored.
        await CloseCrashedSessionAsync();
    }

    private async Task PersistTimelineAsync()
    {
        if (_timeline is null)
            return;

        try
        {
            await _timeline.FlushAsync(upload: false);
        }
        catch
        {
            // Best-effort; the crash event is what matters.
        }
    }

    private async Task CloseCrashedSessionAsync()
    {
        if (_sessions is null)
            return;

        try
        {
            await _sessions.CaptureCrashAsync();
        }
        catch
        {
            // Release health is best-effort; the crash event is what matters.
        }
    }

    /// <summary>Keeps a non-fatal event with probability <see cref="AvizoroOptions.SampleRate"/>.</summary>
    private bool Sampled()
    {
        var rate = _options.SampleRate;
        return rate >= 1.0 || (rate > 0.0 && Random.Shared.NextDouble() < rate);
    }

    /// <summary>
    /// Runs the user's BeforeSend hook, then PII scrubbing. Returns null when the
    /// hook dropped the event. A throwing hook is ignored (event sent as-is).
    /// </summary>
    private AvizoroEvent? Process(AvizoroEvent avizoroEvent)
    {
        if (_options.BeforeSend is { } beforeSend)
        {
            try
            {
                var result = beforeSend(avizoroEvent);
                if (result is null)
                    return null;
                avizoroEvent = result;
            }
            catch
            {
                // Never let a buggy hook lose the event.
            }
        }

        if (_scrubber is not null)
        {
            try
            {
                _scrubber.Scrub(avizoroEvent);
            }
            catch
            {
            }
        }

        return avizoroEvent;
    }

    /// <summary>
    /// If screen capture is enabled and a recent frame exists, persist it to the
    /// screenshot store keyed by the event id. The uploader ships it after the
    /// event is delivered.
    /// </summary>
    private async Task AttachScreenshotAsync(AvizoroEvent avizoroEvent)
    {
        if (!_options.EnableScreenCapture)
            return;

        try
        {
            var jpeg = _lastScreen.GetLastScreenshot();
            if (jpeg is { Length: > 0 })
                await _screenshots.SaveAsync(avizoroEvent.Id, jpeg);
        }
        catch
        {
            // Screenshot is best-effort; never let it interfere with the event.
        }
    }

    private static AvizoroEvent BuildExceptionEvent(ExceptionInfo exception)
    {
        return new AvizoroEvent
        {
            EventType = EventType.Exception,
            Level = SeverityLevel.Error,
            Exception = exception
        };
    }

    /// <summary>
    /// Single source of truth for application, device, runtime and scope
    /// context. Every event passes through here, so no capture site needs to
    /// remember to fill it in.
    /// </summary>
    private void Enrich(AvizoroEvent avizoroEvent)
    {
        avizoroEvent.ProjectId = _options.ProjectId;
        avizoroEvent.ApplicationName = _options.ApplicationName;
        avizoroEvent.ApplicationVersion = _options.ApplicationVersion;
        avizoroEvent.Environment = _options.Environment;
        avizoroEvent.Release = string.IsNullOrWhiteSpace(_options.Release)
            ? _options.ApplicationVersion
            : _options.Release;

        avizoroEvent.Platform = _device.Platform;
        avizoroEvent.DeviceName = _device.DeviceName;
        avizoroEvent.DeviceModel = _device.Model;
        avizoroEvent.Manufacturer = _device.Manufacturer;
        avizoroEvent.OperatingSystem = _device.OperatingSystem;

        // Place errors and logs on the session timeline.
        avizoroEvent.SessionId ??= SafeCurrentSessionId();

        // Runtime snapshot + scope.
        avizoroEvent.DeviceRuntime = SafeCaptureRuntime();

        // Ambient scope (e.g. the current request) first, then global scope.
        var scope = AvizoroScope.Current;
        avizoroEvent.UserId ??= scope?.UserId ?? _userId;

        if (scope is not null)
        {
            foreach (var kv in scope.Tags)
                avizoroEvent.Tags.TryAdd(kv.Key, kv.Value);
        }
        foreach (var kv in _tags)
            avizoroEvent.Tags.TryAdd(kv.Key, kv.Value);

        // Attach the breadcrumb trail (only if the caller didn't supply one).
        if (avizoroEvent.Breadcrumbs.Count == 0)
            avizoroEvent.Breadcrumbs = (scope?.Breadcrumbs() ?? _breadcrumbs.Snapshot()).ToList();
    }

    private Guid? SafeCurrentSessionId()
    {
        try
        {
            return _sessions?.Current?.SessionId;
        }
        catch
        {
            return null;
        }
    }

    private DeviceRuntime? SafeCaptureRuntime()
    {
        // Never let context capture take down the actual event.
        try
        {
            return _runtime.Capture();
        }
        catch
        {
            return null;
        }
    }
}
