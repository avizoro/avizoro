using Avizoro.Core.Diagnostics;
using Avizoro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avizoro.Hosting;

/// <summary>
/// Start/stop sequence shared by the generic-host service and
/// <see cref="AvizoroSdk.Init"/>: on start register crash handlers, drain
/// events left from the previous run and begin the session; on stop end the
/// session, ship buffered performance spans and give the queue a bounded chance
/// to upload.
/// </summary>
public sealed class AvizoroLifecycle
{
    private readonly IServiceProvider _services;
    private int _started;
    private int _stopped;

    public AvizoroLifecycle(IServiceProvider services) => _services = services;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        var options = _services.GetRequiredService<AvizoroHostOptions>();
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

        var options = _services.GetRequiredService<AvizoroHostOptions>();
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

/// <summary>Runs <see cref="AvizoroLifecycle"/> inside a generic host (ASP.NET Core, workers).</summary>
internal sealed class AvizoroHostedService(AvizoroLifecycle lifecycle) : IHostedService
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
    /// Starts Avizoro (crash handlers, queue drain, session) for hosts that
    /// don't run <c>IHostedService</c>s, such as Blazor WebAssembly:
    /// <c>var host = builder.Build(); host.Services.StartAvizoro(); await host.RunAsync();</c>
    /// </summary>
    public static IServiceProvider StartAvizoro(this IServiceProvider services)
    {
        services.GetRequiredService<AvizoroLifecycle>().Start();
        return services;
    }
}
