#if IOS || MACCATALYST
using Avizoro.Core.Models;
using ObjCRuntime;

namespace Avizoro.Maui.CrashReporting;

public partial class CrashReporter
{
    partial void RegisterPlatformHandlers()
    {
        // Shared by iOS and Mac Catalyst (kept outside Platforms/iOS so both TFMs
        // compile it). Catches managed exceptions as they cross into native
        // (Objective-C) code, which is where most iOS crashes surface in a .NET MAUI app. Pure
        // managed unhandled exceptions are already handled by the shared
        // AppDomain hook in CrashReporter.cs.
        Runtime.MarshalManagedException += OnMarshalManagedException;
    }

    private void OnMarshalManagedException(object? sender, MarshalManagedExceptionEventArgs args)
    {
        // Persist synchronously (local write, fast) so the crash isn't lost if
        // the process is torn down right after. Deduped against the managed
        // AppDomain handler so the same crash isn't reported twice.
        CaptureFatal(ExceptionInfo.FromException(args.Exception));
    }
}
#endif
