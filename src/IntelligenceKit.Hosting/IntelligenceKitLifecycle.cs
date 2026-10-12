using IntelligenceKit.Core.Diagnostics;
using IntelligenceKit.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IntelligenceKit.Hosting;

/// <summary>
/// Start/stop sequence shared by the generic-host service and
/// <see cref="IntelligenceKitSdk.Init"/>: on start register crash handlers, drain
/// events left from the previous run and begin the session; on stop end the
/// session, ship buffered performance spans and give the queue a bounded chance
/// to upload.
/// </summary>
public sealed class IntelligenceKitLifecycle
{
    private readonly IServiceProvider _services;
    private int _started;
    private int _stopped;

    public IntelligenceKitLifecycle(IServiceProvider services) => _services = services;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        var options = _services.GetRequiredService<IntelligenceKitHostOptions>();
        if (options.EnableCrashHandlers)
            _services.GetRequiredService<ICrashReporter>().Register();

        _ = _services.GetRequiredService<IEventUploader>().FlushAsync();
        _ = _services.GetService<ISessionTracker>()?.StartAsync();
        _services.GetService<UiThreadWatchdog>()?.Start();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
            return;

        var options = _services.GetRequiredService<IntelligenceKitHostOptions>();
        _services.GetService<UiThreadWatchdog>()?.Dispose();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.ShutdownFlushTimeout);

        try
        {
            if (_services.GetService<ISessionTimeline>() is { } timeline)
                await timeline.FlushAsync(upload: false).WaitAsync(timeout.Token).ConfigureAwait(false);
            if (_services.GetService<ISessionTracker>() is { } sessions)
                await sessions.EndAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
            if (_services.GetService<IPerformanceMonitor>() is { } performance)
                await performance.FlushAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
            await _services.GetRequiredService<IEventUploader>().FlushAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Out of time: whatever is left stays queued for the next start.
        }
    }
}

/// <summary>Runs <see cref="IntelligenceKitLifecycle"/> inside a generic host (ASP.NET Core, workers).</summary>
internal sealed class IntelligenceKitHostedService(IntelligenceKitLifecycle lifecycle) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lifecycle.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => lifecycle.StopAsync(cancellationToken);
}

public static class ServiceProviderExtensions
{
    /// <summary>
    /// Starts IntelligenceKit (crash handlers, queue drain, session) for hosts that
    /// don't run <c>IHostedService</c>s, such as Blazor WebAssembly:
    /// <c>var host = builder.Build(); host.Services.StartIntelligenceKit(); await host.RunAsync();</c>
    /// </summary>
    public static IServiceProvider StartIntelligenceKit(this IServiceProvider services)
    {
        services.GetRequiredService<IntelligenceKitLifecycle>().Start();
        return services;
    }
}
