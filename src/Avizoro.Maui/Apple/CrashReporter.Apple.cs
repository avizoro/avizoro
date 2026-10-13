#if IOS
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avizoro.Core.Models;
using Foundation;
using Microsoft.Maui.Storage;
using ObjCRuntime;

namespace Avizoro.Maui.CrashReporting;

public unsafe partial class CrashReporter
{
    // The uncaught-exception callback is a plain C function pointer, so it reaches
    // the reporter through statics.
    private static CrashReporter? s_instance;
    private static delegate* unmanaged[Cdecl]<IntPtr, void> s_previousUncaughtHandler;

    partial void RegisterPlatformHandlers()
    {
        // Catches managed exceptions as they cross into native
        // (Objective-C) code, which is where most iOS crashes surface in a .NET MAUI app. Pure
        // managed unhandled exceptions are already handled by the shared
        // AppDomain hook in CrashReporter.cs.
        Runtime.MarshalManagedException += OnMarshalManagedException;

        if (!_options.EnableNativeCrashCapture)
            return;

        RegisterNativeHandlers();
    }

    private void RegisterNativeHandlers()
    {
        try
        {
            var directory = FileSystem.Current.AppDataDirectory;

            // A signal recorded by the previous run becomes a crash event now.
            // Both files are consumed even when there is nothing to report, so a
            // stale session snapshot is never attributed to a later crash.
            var previous = NativeCrashMarker.TakePrevious(directory);
            var previousSession = NativeCrashMarker.TakePreviousSession(directory);
            if (previous is { } crash)
                ReportPreviousRunCrash(crash, previousSession);

            // Objective-C exceptions that reach managed code and are about to abort
            // the process. The other modes turn them into managed exceptions, which
            // the app may catch; uncaught ones arrive through the AppDomain hook.
            Runtime.MarshalObjectiveCException += OnMarshalObjectiveCException;

            // Objective-C exceptions that never cross into managed code (thrown and
            // left unhandled on a native thread or below the run loop).
            s_instance = this;
            s_previousUncaughtHandler = NSGetUncaughtExceptionHandler();
            NSSetUncaughtExceptionHandler(&OnUncaughtObjectiveCException);

            // Under a debugger the signals belong to it (breakpoints raise SIGTRAP).
            if (!Debugger.IsAttached)
                NativeCrashMarker.Install(directory);
        }
        catch
        {
            // Never let crash-capture setup take the app down.
        }
    }

    partial void OnFatalCaptured() => NativeCrashMarker.MarkReported();

    private void OnMarshalManagedException(object? sender, MarshalManagedExceptionEventArgs args)
    {
        // Persist synchronously (local write, fast) so the crash isn't lost if
        // the process is torn down right after. Deduped against the managed
        // AppDomain handler so the same crash isn't reported twice.
        CaptureFatal(ExceptionInfo.FromException(args.Exception));
    }

    private void OnMarshalObjectiveCException(object? sender, MarshalObjectiveCExceptionEventArgs args)
    {
        if (args.ExceptionMode == MarshalObjectiveCExceptionMode.Abort && args.Exception is { } exception)
            CaptureFatal(FromNSException(exception));
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnUncaughtObjectiveCException(IntPtr handle)
    {
        try
        {
            if (Runtime.GetNSObject<NSException>(handle) is { } exception)
                s_instance?.CaptureFatal(FromNSException(exception));
        }
        catch
        {
            // Last-gasp best effort: never throw from a crash handler.
        }

        // Hand over to whoever was registered before us (another SDK, or nobody);
        // the Objective-C runtime aborts the process afterwards either way.
        var previous = s_previousUncaughtHandler;
        if (previous is not null)
            previous(handle);
    }

    private void ReportPreviousRunCrash(NativeCrashMarker.PreviousCrash crash, SessionInfo? session)
    {
        var name = NativeCrashMarker.Name(crash.Signal);
        var exception = new ExceptionInfo
        {
            Type = name,
            Message = $"The app was terminated by {name} in native code. Only the signal was recorded, so there is no stack trace.",
            Source = "signal"
        };

        try
        {
            // A local write, bounded like the live crash path, so it is in the queue
            // before the startup flush runs.
            Task.Run(() => _avizoro.CapturePreviousRunCrashAsync(exception, crash.OccurredAt, session))
                .Wait(TimeSpan.FromSeconds(2));
            CrashFeedbackPrompt.RememberCrash(_avizoro.LastEventId);
        }
        catch
        {
            // Reporting an old crash must never break this launch.
        }
    }

    private static ExceptionInfo FromNSException(NSException exception)
    {
        return new ExceptionInfo
        {
            Type = string.IsNullOrEmpty(exception.Name) ? "NSException" : exception.Name,
            Message = exception.Reason ?? string.Empty,
            StackTrace = string.Join('\n', exception.CallStackSymbols ?? []),
            Source = "NSException"
        };
    }

    [DllImport(Constants.FoundationLibrary)]
    private static extern void NSSetUncaughtExceptionHandler(delegate* unmanaged[Cdecl]<IntPtr, void> handler);

    [DllImport(Constants.FoundationLibrary)]
    private static extern delegate* unmanaged[Cdecl]<IntPtr, void> NSGetUncaughtExceptionHandler();
}
#endif
