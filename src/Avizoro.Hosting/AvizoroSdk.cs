using Avizoro.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Avizoro.Hosting;

/// <summary>
/// Static entry point for apps without a DI container (classic WPF/WinForms,
/// console tools):
/// <code>
/// using var ik = AvizoroSdk.Init("http://key@host:7099/my-app");
/// ...
/// AvizoroSdk.Current?.TrackExceptionAsync(ex);
/// </code>
/// Disposing the returned instance ends the session and flushes pending events.
/// </summary>
public static class AvizoroSdk
{
    private static AvizoroInstance? _instance;

    /// <summary>The initialized SDK, or null before <see cref="Init"/>.</summary>
    public static IAvizoro? Current => _instance?.Kit;

    /// <summary>The initialized instance, or null before <see cref="Init"/>.</summary>
    public static AvizoroInstance? Instance => _instance;

    /// <summary>
    /// Builds and starts Avizoro. Calling it again returns the existing
    /// instance. <paramref name="configureServices"/> lets platform SDKs add their
    /// own registrations (UI dispatcher, crash hooks) before the container is built.
    /// </summary>
    public static AvizoroInstance Init(string dsn, Action<AvizoroHostOptions>? configure = null,
        Action<IServiceCollection, AvizoroHostOptions>? configureServices = null)
    {
        if (_instance is { } existing)
            return existing;

        var services = new ServiceCollection();
        var options = services.AddAvizoroCore(dsn, configure);
        configureServices?.Invoke(services, options);

        var instance = new AvizoroInstance(services.BuildServiceProvider());
        if (Interlocked.CompareExchange(ref _instance, instance, null) is { } raced)
        {
            instance.Dispose();
            return raced;
        }

        instance.Lifecycle.Start();
        return instance;
    }

    internal static void Clear(AvizoroInstance instance)
        => Interlocked.CompareExchange(ref _instance, null, instance);
}

/// <summary>An initialized SDK and its services. Dispose on app exit.</summary>
public sealed class AvizoroInstance : IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    internal AvizoroInstance(ServiceProvider provider)
    {
        _provider = provider;
        Kit = provider.GetRequiredService<IAvizoro>();
        Lifecycle = provider.GetRequiredService<AvizoroLifecycle>();
    }

    public IAvizoro Kit { get; }

    public IServiceProvider Services => _provider;

    internal AvizoroLifecycle Lifecycle { get; }

    public async ValueTask DisposeAsync()
    {
        await Lifecycle.StopAsync().ConfigureAwait(false);
        AvizoroSdk.Clear(this);
        await _provider.DisposeAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            // Bounded by ShutdownFlushTimeout; run off any UI context to avoid deadlocks.
            Task.Run(() => Lifecycle.StopAsync()).GetAwaiter().GetResult();
        }
        catch
        {
        }
        AvizoroSdk.Clear(this);
        _provider.Dispose();
    }
}
