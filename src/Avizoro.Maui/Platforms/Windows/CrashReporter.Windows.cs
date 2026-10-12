using Avizoro.Core.Models;

namespace Avizoro.Maui.CrashReporting;

public partial class CrashReporter
{
    partial void RegisterPlatformHandlers()
    {
        // WinUI reports exceptions thrown on the UI thread here rather than through
        // AppDomain.UnhandledException, and terminates the app unless a handler marks
        // them handled.
        if (Microsoft.UI.Xaml.Application.Current is { } app)
            app.UnhandledException += OnWinUIUnhandledException;
    }

    private void OnWinUIUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        if (e.Exception is null)
            return;

        var info = ExceptionInfo.FromException(e.Exception);
        if (e.Handled)
        {
            // Another handler already recovered: record it as a handled error.
            _ = _avizoro.TrackExceptionAsync(info);
            return;
        }

        // Not handled (yet): WinUI will tear the process down, so persist it now as
        // a crash — locally, without a network call — for upload on next launch.
        CaptureFatal(info);
    }
}
