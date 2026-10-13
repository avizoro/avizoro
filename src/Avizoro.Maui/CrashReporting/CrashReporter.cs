using Avizoro.Core.Configuration;
using Avizoro.Core.Models;
using Avizoro.Core.Services;

namespace Avizoro.Maui.CrashReporting;

/// <summary>
/// Cross-platform crash reporter. The shared part hooks the managed
/// unhandled-exception sources; each platform adds its own native handler
/// through the <see cref="RegisterPlatformHandlers"/> partial method, so the
/// consumer never writes platform-conditional code.
/// </summary>
public partial class CrashReporter : ICrashReporter
{
    private readonly IAvizoro _avizoro;
    private readonly AvizoroOptions _options;

    // Latches the first fatal capture. A single fatal crash can surface through
    // more than one handler (managed AppDomain + the platform's native handler),
    // so we report it once. The process dies right after a fatal crash, so a
    // one-shot latch for the process lifetime is safe.
    private int _fatalCaptured;

    public CrashReporter(IAvizoro avizoro, AvizoroOptions options)
    {
        _avizoro = avizoro;
        _options = options;
    }

    public void Register()
    {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        RegisterPlatformHandlers();
    }

    public Task ReportAsync(ExceptionInfo exception)
    {
        // Crash reports are persisted locally and uploaded on the next launch.
        return _avizoro.CaptureCrashAsync(exception);
    }

    /// <summary>
    /// Persists a fatal exception synchronously from a crash handler. Writing to
    /// the local store is fast and reliable, so we block briefly to guarantee it
    /// lands before the process terminates — WITHOUT any network call. Runs on
    /// the thread pool to avoid deadlocking on the UI SynchronizationContext and
    /// is bounded by a short timeout as a safety net.
    /// </summary>
    internal void CaptureBlocking(ExceptionInfo exception)
    {
        try
        {
            Task.Run(() => _avizoro.CaptureCrashAsync(exception)).Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Last-gasp best effort: never throw from a crash handler.
        }
    }

    /// <summary>
    /// Captures a fatal crash exactly once, even when several handlers observe
    /// the same crash. Callers should still do any platform teardown (e.g.
    /// chaining to the previous handler) regardless of the return value.
    /// </summary>
    internal void CaptureFatal(ExceptionInfo exception)
    {
        if (Interlocked.Exchange(ref _fatalCaptured, 1) != 0)
            return;

        OnFatalCaptured();
        CaptureBlocking(exception);

        // Lets the next launch ask the user what happened (if enabled).
        CrashFeedbackPrompt.RememberCrash(_avizoro.LastEventId);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            CaptureFatal(Describe(ex));
        }
        // Do not swallow: the runtime proceeds to terminate as usual.
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Not fatal: the process keeps running, so report it as a handled error
        // (a crash capture would wrongly mark the session as crashed).
        _ = _avizoro.TrackExceptionAsync(ExceptionInfo.FromException(e.Exception));
        e.SetObserved();
    }

    /// <summary>
    /// Turns a fatal managed exception into the reported shape. A platform can
    /// replace the default when the managed exception only wraps a native one.
    /// </summary>
    private ExceptionInfo Describe(Exception exception)
    {
        ExceptionInfo? described = null;
        try
        {
            DescribePlatformException(exception, ref described);
        }
        catch
        {
            // Fall back to the managed description.
        }

        return described ?? ExceptionInfo.FromException(exception);
    }

    /// <summary>
    /// Implemented per platform under Platforms/. No-op on platforms that
    /// don't provide a platform-specific file.
    /// </summary>
    partial void RegisterPlatformHandlers();

    /// <summary>
    /// Lets a platform describe a managed exception that wraps a native one
    /// (e.g. an Objective-C exception) by its native name, reason and stack.
    /// </summary>
    partial void DescribePlatformException(Exception exception, ref ExceptionInfo? described);

    /// <summary>
    /// Runs once, right before the first fatal crash is persisted. Lets a platform
    /// tell its lower-level handlers (e.g. signals) that this crash is already reported.
    /// </summary>
    partial void OnFatalCaptured();
}
