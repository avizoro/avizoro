using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Providers;
using Avizoro.Core.Services;
using Avizoro.Core.Storage;
using Avizoro.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avizoro.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Avizoro for a generic .NET host from a DSN
    /// (<c>http://projectKey@host:port/projectId</c>). App name/version come from
    /// the entry assembly. Registers a hosted service that starts crash capture and
    /// sessions and flushes on shutdown — for hosts without one (desktop apps
    /// without the generic host), use <see cref="AvizoroSdk.Init"/> instead.
    /// </summary>
    public static IServiceCollection AddAvizoro(this IServiceCollection services, string dsn,
        Action<AvizoroHostOptions>? configure = null)
    {
        services.AddAvizoroCore(dsn, configure);
        services.AddHostedService<AvizoroHostedService>();
        return services;
    }

    /// <summary>
    /// Same registrations as <see cref="AddAvizoro"/> minus the hosted
    /// service — for hosts that drive <see cref="AvizoroLifecycle"/> themselves.
    /// </summary>
    public static AvizoroHostOptions AddAvizoroCore(this IServiceCollection services, string dsn,
        Action<AvizoroHostOptions>? configure = null)
    {
        var parsed = AvizoroDsn.Parse(dsn);
        var (name, version) = HostDefaults.EntryApplication();

        var options = new AvizoroHostOptions
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
        services.TryAddSingleton<AvizoroOptions>(sp => sp.GetRequiredService<AvizoroHostOptions>());

        services.AddHttpClient<IAvizoroClient, HttpAvizoroClient>();
        services.TryAddSingleton<IAvizoro, AvizoroService>();
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
        services.TryAddSingleton<AvizoroLifecycle>();

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
            services.AddLogging(logging => logging.AddAvizoro());

        return options;
    }

    /// <summary>
    /// Records requests made through this client as <c>http</c> breadcrumbs and
    /// performance spans: <c>services.AddHttpClient&lt;MyApi&gt;().AddAvizoroHandler()</c>.
    /// </summary>
    public static IHttpClientBuilder AddAvizoroHandler(this IHttpClientBuilder builder)
        => builder.AddHttpMessageHandler(sp => new AvizoroHttpHandler(
            sp.GetRequiredService<IAvizoro>(),
            sp.GetService<IPerformanceMonitor>()));
}
