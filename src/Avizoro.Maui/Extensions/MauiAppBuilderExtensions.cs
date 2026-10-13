using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Providers;
using Avizoro.Core.Services;
using Avizoro.Core.Storage;
using Avizoro.Extensions.Logging;
using Avizoro.Maui.CrashReporting;
using Avizoro.Maui.Diagnostics;
using Avizoro.Maui.Providers;
using Avizoro.Maui.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;

namespace Avizoro.Maui.Extensions;

public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers Avizoro using a single DSN string, e.g.
    /// <c>http://projectKey@host:5000/projectId</c>. Application name and
    /// version are auto-detected from <see cref="AppInfo"/>. Pass
    /// <paramref name="configure"/> only for advanced overrides.
    /// </summary>
    public static MauiAppBuilder UseAvizoro(this MauiAppBuilder builder, string dsn, Action<AvizoroOptions>? configure = null)
    {
        var parsed = AvizoroDsn.Parse(dsn);

        var options = new AvizoroOptions
        {
            ServerUrl = parsed.ServerUrl,
            ProjectKey = parsed.ProjectKey,
            ProjectId = parsed.ProjectId,
            ApplicationName = AppInfo.Current.Name,
            ApplicationVersion = AppInfo.Current.VersionString
        };

        configure?.Invoke(options);

        builder.Services.AddSingleton(options);

        builder.Services.AddHttpClient<IAvizoroClient, HttpAvizoroClient>();

        // Release health: session tracking driven by the app lifecycle.
        builder.Services.AddSingleton<IInstallationIdProvider, MauiInstallationIdProvider>();
        if (options.EnableAutoSessionTracking)
            builder.Services.AddSingleton<ISessionTracker, SessionTracker>();

        // Session timeline: the whole session's activity, not only the trail attached to
        // errors. Needs sessions; the breadcrumb buffer and the service pick it up by DI.
        if (options.EnableSessionTimeline && options.EnableAutoSessionTracking)
        {
            builder.Services.AddSingleton<SessionTimelineRecorder>();
            builder.Services.AddSingleton<ISessionTimeline>(sp => sp.GetRequiredService<SessionTimelineRecorder>());
        }

        // ILogger integration: logs become breadcrumbs (Information+) and events (Error+).
        if (options.EnableLoggingIntegration)
            builder.Logging.AddAvizoro();

        // Performance: app start / page load timing, plus HTTP spans for clients that
        // opt in via AddAvizoroHandler().
        if (options.EnablePerformanceMonitoring)
            builder.Services.AddSingleton<IPerformanceMonitor, PerformanceMonitor>();

        // Frozen-UI (ANR) detection: a background watchdog pinging the main thread.
        if (options.EnableAnrDetection)
        {
            builder.Services.AddSingleton<IUiThreadDispatcher, MauiUiThreadDispatcher>();
            builder.Services.AddSingleton<UiThreadWatchdog>();
        }

        if (options.EnableAutoSessionTracking || options.EnableAnrDetection || options.EnablePerformanceMonitoring)
            builder.ConfigureLifecycleEvents(RegisterAppLifecycle);
        builder.Services.AddSingleton<IAvizoro, AvizoroService>();
        builder.Services.AddSingleton<IDeviceContextProvider, MauiDeviceContextProvider>();

        // Rich context: breadcrumb ring buffer, navigation trail, runtime snapshot.
        builder.Services.AddSingleton<IBreadcrumbBuffer, BreadcrumbBuffer>();
        builder.Services.AddSingleton<NavigationTracker>();
        builder.Services.AddSingleton<IRuntimeContextProvider, MauiRuntimeContextProvider>();

        // Screen capture: proactive "last screen" buffer + capture service.
        builder.Services.AddSingleton<LastScreenBuffer>();
        builder.Services.AddSingleton<ILastScreenProvider>(sp => sp.GetRequiredService<LastScreenBuffer>());
        builder.Services.AddSingleton<ScreenCaptureService>();
        builder.Services.AddSingleton<IScreenCapture>(sp => sp.GetRequiredService<ScreenCaptureService>());

        // Offline store-and-forward: persist events to SQLite, drain via the uploader.
        var databasePath = Path.Combine(FileSystem.Current.AppDataDirectory, "avizoro.db3");
        MoveLegacyDatabase(Path.Combine(FileSystem.Current.AppDataDirectory, "intelligencekit.db3"), databasePath);
        builder.Services.AddSingleton<IEventStore>(_ => new SqliteEventStore(databasePath));
        builder.Services.AddSingleton<IScreenshotStore>(_ => new SqliteScreenshotStore(databasePath));
        builder.Services.AddSingleton<IEventUploader, EventUploader>();

        // Crash reporting + startup work (register handlers, initial flush,
        // flush on reconnect). Activated automatically; no host-app code needed.
        builder.Services.AddSingleton<ICrashReporter, CrashReporter>();
        builder.Services.AddSingleton<CrashFeedbackPrompt>();
        builder.Services.AddSingleton<IMauiInitializeService, AvizoroStartup>();

        return builder;
    }

    /// <summary>
    /// Foreground/background hooks: the session is paused in the background and
    /// resumed (or restarted, after <c>SessionTimeout</c>) when the app returns; the
    /// ANR watchdog only watches while the app is in the foreground.
    /// </summary>
    /// <summary>Keeps the offline queue of an app upgraded from 1.x (IntelligenceKit).</summary>
    private static void MoveLegacyDatabase(string legacy, string current)
    {
        try
        {
            if (File.Exists(legacy) && !File.Exists(current))
                File.Move(legacy, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a fresh database only loses unsent events.
        }
    }

    private static void RegisterAppLifecycle(ILifecycleBuilder events)
    {
#if ANDROID
        events.AddAndroid(android => android
            .OnStart(activity => OnForeground())
            .OnStop(activity => OnBackground()));
#elif IOS || MACCATALYST
        events.AddiOS(ios => ios
            .WillEnterForeground(application => OnForeground())
            .DidEnterBackground(application => OnBackground()));
#elif WINDOWS
        // Desktop has no background state; a minimized/hidden window is the
        // closest equivalent, and closing the window ends the run.
        events.AddWindows(windows => windows
            .OnVisibilityChanged((window, args) =>
            {
                if (args.Visible)
                    OnForeground();
                else
                    OnBackground();
            })
            .OnClosed((window, args) => OnClosing()));
#endif
    }

    private static void OnForeground()
    {
        var services = IPlatformApplication.Current?.Services;
        var sessions = services?.GetService<ISessionTracker>();
        _ = sessions?.ResumeAsync();
#if IOS || MACCATALYST
        // Resuming after the timeout begins a new session.
        NativeCrashMarker.RememberSession(sessions?.Current);
#endif
        services?.GetService<UiThreadWatchdog>()?.Start();
    }

    private static void OnBackground() => _ = BackgroundAsync();

    /// <summary>
    /// The process is about to exit (desktop window closed): give the session update
    /// and buffered spans a bounded moment to reach the local queue. Runs off the UI
    /// thread so awaiting inside can't deadlock on it.
    /// </summary>
    private static void OnClosing()
    {
        try
        {
            Task.Run(BackgroundAsync).Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
    }

    private static Task BackgroundAsync()
    {
        var services = IPlatformApplication.Current?.Services;
        services?.GetService<UiThreadWatchdog>()?.Pause();
        return Task.WhenAll(
            services?.GetService<ISessionTimeline>()?.FlushAsync() ?? Task.CompletedTask,
            services?.GetService<ISessionTracker>()?.PauseAsync() ?? Task.CompletedTask,
            services?.GetService<IPerformanceMonitor>()?.FlushAsync() ?? Task.CompletedTask);
    }
}
