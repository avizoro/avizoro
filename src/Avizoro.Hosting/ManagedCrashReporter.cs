using Avizoro.Core.Models;
using Avizoro.Core.Services;

namespace Avizoro.Hosting;

/// <summary>
/// Crash capture for managed hosts: <c>AppDomain.UnhandledException</c> (fatal) and
/// <c>TaskScheduler.UnobservedTaskException</c> (non-fatal). UI SDKs call
/// <see cref="CaptureFatal"/> / <see cref="CaptureHandled"/> from their own
/// dispatcher hooks (WPF, WinForms, Avalonia). A fatal crash is persisted
/// synchronously to the offline queue — no network call on a dying process — and
/// uploaded on the next start.
/// </summary>
public sealed class ManagedCrashReporter : ICrashReporter
{
    private readonly IAvizoro _kit;
    private int _registered;
    private int _fatalCaptured;

    public ManagedCrashReporter(IAvizoro kit) => _kit = kit;

    /// <summary>True once a fatal crash was captured (the process is going down).</summary>
    public bool FatalCaptured => Volatile.Read(ref _fatalCaptured) != 0;

    public void Register()
    {
        if (Interlocked.Exchange(ref _registered, 1) != 0)
            return;

        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    public Task ReportAsync(ExceptionInfo exception) => _kit.CaptureCrashAsync(exception);

    /// <summary>Records a fatal crash once per process, blocking briefly for the local write.</summary>
    public void CaptureFatal(Exception exception)
    {
        if (Interlocked.Exchange(ref _fatalCaptured, 1) != 0)
            return;

        try
        {
            var capture = Task.Run(() => _kit.CaptureCrashAsync(ExceptionInfo.FromException(exception)));
            if (!OperatingSystem.IsBrowser()) // single-threaded: can't block there
                capture.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Never throw from a crash handler.
        }
    }

    /// <summary>Reports an exception the app survived (e.g. one a UI dispatcher marked handled).</summary>
    public void CaptureHandled(Exception exception)
    {
        try
        {
            var capture = Task.Run(() => _kit.TrackExceptionAsync(exception));
            if (!OperatingSystem.IsBrowser())
                capture.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            CaptureFatal(ex);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CaptureHandled(e.Exception);
        e.SetObserved();
    }
}
