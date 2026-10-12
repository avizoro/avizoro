using IntelligenceKit.Core.Configuration;
using IntelligenceKit.Core.Diagnostics;
using IntelligenceKit.Core.Providers;
using IntelligenceKit.Core.Services;
using IntelligenceKit.Core.Storage;
using IntelligenceKit.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IntelligenceKit.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers IntelligenceKit for a generic .NET host from a DSN
    /// (<c>http://projectKey@host:port/projectId</c>). App name/version come from
    /// the entry assembly. Registers a hosted service that starts crash capture and
    /// sessions and flushes on shutdown — for hosts without one (desktop apps
    /// without the generic host), use <see cref="IntelligenceKitSdk.Init"/> instead.
    /// </summary>
    public static IServiceCollection AddIntelligenceKit(this IServiceCollection services, string dsn,
        Action<IntelligenceKitHostOptions>? configure = null)
    {
        services.AddIntelligenceKitCore(dsn, configure);
        services.AddHostedService<IntelligenceKitHostedService>();
        return services;
    }

    /// <summary>
    /// Same registrations as <see cref="AddIntelligenceKit"/> minus the hosted
    /// service — for hosts that drive <see cref="IntelligenceKitLifecycle"/> themselves.
    /// </summary>
    public static IntelligenceKitHostOptions AddIntelligenceKitCore(this IServiceCollection services, string dsn,
        Action<IntelligenceKitHostOptions>? configure = null)
    {
        var parsed = IntelligenceDsn.Parse(dsn);
        var (name, version) = HostDefaults.EntryApplication();

        var options = new IntelligenceKitHostOptions
        {
            ServerUrl = parsed.ServerUrl,
            ProjectKey = parsed.ProjectKey,
            ProjectId = parsed.ProjectId,
            ApplicationName = name,
            ApplicationVersion = version,
        };
        configure?.Invoke(options);

        var storage = options.OfflineStorePath ?? HostDefaults.StorageDirectory(options.ProjectId);

        services.TryAddSingleton(options);
        services.TryAddSingleton<IntelligenceOptions>(sp => sp.GetRequiredService<IntelligenceKitHostOptions>());

        services.AddHttpClient<IIntelligenceClient, HttpIntelligenceClient>();
        services.TryAddSingleton<IIntelligenceKit, IntelligenceKitService>();
        services.TryAddSingleton<IDeviceContextProvider, EnvironmentDeviceContextProvider>();
        services.TryAddSingleton<IRuntimeContextProvider, ProcessRuntimeContextProvider>();
        services.TryAddSingleton<IBreadcrumbBuffer, BreadcrumbBuffer>();
        services.TryAddSingleton<ILastScreenProvider, NullLastScreenProvider>();
        services.TryAddSingleton<IScreenshotStore, NullScreenshotStore>();
        services.TryAddSingleton<IEventUploader, EventUploader>();

        if (options.UseInMemoryStore)
        {
            services.TryAddSingleton<IEventStore>(new InMemoryEventStore());
            services.TryAddSingleton<IInstallationIdProvider, ProcessInstallationIdProvider>();
        }
        else
        {
            services.TryAddSingleton<IEventStore>(_ => new FileEventStore(Path.Combine(storage, "queue")));
            services.TryAddSingleton<IInstallationIdProvider>(_ => new FileInstallationIdProvider(storage));
        }

        services.TryAddSingleton<ManagedCrashReporter>();
        services.TryAddSingleton<ICrashReporter>(sp => sp.GetRequiredService<ManagedCrashReporter>());
        services.TryAddSingleton<IntelligenceKitLifecycle>();

        if (options.EnableAutoSessionTracking)
            services.TryAddSingleton<ISessionTracker, SessionTracker>();

        // Session timeline (desktop apps): needs sessions; the breadcrumb buffer and the
        // service pick it up by DI.
        if (options.EnableSessionTimeline && options.EnableAutoSessionTracking)
        {
            services.TryAddSingleton<SessionTimelineRecorder>();
            services.TryAddSingleton<ISessionTimeline>(sp => sp.GetRequiredService<SessionTimelineRecorder>());
        }

        if (options.EnablePerformanceMonitoring)
            services.TryAddSingleton<IPerformanceMonitor, PerformanceMonitor>();

        if (options.EnableLoggingIntegration)
            services.AddLogging(logging => logging.AddIntelligenceKit());

        return options;
    }

    /// <summary>
    /// Records requests made through this client as <c>http</c> breadcrumbs and
    /// performance spans: <c>services.AddHttpClient&lt;MyApi&gt;().AddIntelligenceKitHandler()</c>.
    /// </summary>
    public static IHttpClientBuilder AddIntelligenceKitHandler(this IHttpClientBuilder builder)
        => builder.AddHttpMessageHandler(sp => new IntelligenceKitHttpHandler(
            sp.GetRequiredService<IIntelligenceKit>(),
            sp.GetService<IPerformanceMonitor>()));
}
