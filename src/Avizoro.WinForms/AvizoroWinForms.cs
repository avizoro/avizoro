using System.Windows.Forms;
using Avizoro.Hosting;

namespace Avizoro.WinForms;

/// <summary>
/// Avizoro for Windows Forms. Call first thing in <c>Main</c>, before any
/// form is created:
/// <code>
/// [STAThread]
/// static void Main()
/// {
///     using var ik = AvizoroWinForms.Init("http://key@host:7099/my-winforms-app");
///     ApplicationConfiguration.Initialize();
///     Application.Run(new MainForm());
/// }
/// </code>
/// </summary>
public static class AvizoroWinForms
{
    /// <param name="dsn">Project DSN, e.g. <c>http://key@host:7099/my-app</c>.</param>
    /// <param name="configure">Optional SDK options.</param>
    /// <param name="crashOnUnhandledUiException">
    /// true (default): an exception on the UI thread crashes the app like any other
    /// unhandled exception (and is captured as a crash) instead of WinForms' "continue?"
    /// dialog. false: WinForms keeps running and the exception is captured as a handled
    /// error.
    /// </param>
    public static AvizoroInstance Init(string dsn, Action<AvizoroHostOptions>? configure = null,
        bool crashOnUnhandledUiException = true)
    {
        try
        {
            if (crashOnUnhandledUiException)
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            else
                Application.ThreadException += (_, e) =>
                {
                    if (AvizoroSdk.Instance?.Services.GetService(typeof(ManagedCrashReporter)) is ManagedCrashReporter reporter)
                        reporter.CaptureHandled(e.Exception);
                };
        }
        catch (InvalidOperationException)
        {
            // A window already exists; the mode can no longer change. Crashes are
            // still captured through AppDomain.UnhandledException.
        }

        // WinForms installs its SynchronizationContext with the first control; create
        // one now (on the UI thread) so the ANR watchdog can post to the message loop.
        var uiContext = SynchronizationContext.Current as WindowsFormsSynchronizationContext
            ?? new WindowsFormsSynchronizationContext();

        var instance = AvizoroSdk.Init(dsn, configure, (services, options) =>
            UiHostIntegration.AddUiThreadWatchdog(services, options, uiContext));

        Application.ApplicationExit += (_, _) => instance.Dispose();
        return instance;
    }
}
