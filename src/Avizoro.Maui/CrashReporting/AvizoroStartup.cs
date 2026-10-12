using Avizoro.Core.Diagnostics;
using Avizoro.Core.Services;
using Avizoro.Maui.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Networking;

namespace Avizoro.Maui.CrashReporting;

/// <summary>
/// Runs during <c>MauiApp</c> construction and wires up Avizoro's
/// runtime behavior: activates crash capture, uploads anything left over from a
/// previous session, and re-drains the offline queue whenever connectivity is
/// restored. Keeps all of this out of the host app.
/// </summary>
internal sealed class AvizoroStartup : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        services.GetRequiredService<ICrashReporter>().Register();

        // Begin recording navigation breadcrumbs (records the current screen too).
        services.GetRequiredService<NavigationTracker>().Start();

        // Begin proactive screen capture (no-op unless EnableScreenCapture is set).
        services.GetRequiredService<ScreenCaptureService>().Start();

        // Begin the first session (only registered when auto session tracking is on).
        _ = services.GetService<ISessionTracker>()?.StartAsync();

        // Ask about last session's crash (no-op unless EnableCrashFeedbackPrompt).
        services.GetRequiredService<CrashFeedbackPrompt>().Start();

        // Start watching for a frozen UI (only registered when ANR detection is on).
        services.GetService<UiThreadWatchdog>()?.Start();

        var uploader = services.GetRequiredService<IEventUploader>();

        // Upload events persisted before this launch (e.g. a crash last session).
        _ = uploader.FlushAsync();

        Connectivity.Current.ConnectivityChanged += (_, e) =>
        {
            if (e.NetworkAccess == NetworkAccess.Internet)
                _ = uploader.FlushAsync();
        };
    }
}
