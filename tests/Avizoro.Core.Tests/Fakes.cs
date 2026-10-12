using Avizoro.Core.Diagnostics;
using Avizoro.Core.Models;
using Avizoro.Core.Providers;
using Avizoro.Core.Services;
using Avizoro.Core.Storage;

namespace Avizoro.Core.Tests;

/// <summary>Ordered log of the notable side effects a test exercises, so tests
/// can assert not just <em>that</em> things happened but in what order.</summary>
internal sealed class CallLog
{
    public List<string> Entries { get; } = new();
    public void Record(string entry) => Entries.Add(entry);
}

internal sealed class FakeEventStore : IEventStore
{
    private readonly CallLog _log;
    public List<AvizoroEvent> Saved { get; } = new();

    public FakeEventStore(CallLog log) => _log = log;

    public Task SaveAsync(AvizoroEvent avizoroEvent)
    {
        Saved.Add(avizoroEvent);
        _log.Record("store.save");
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvizoroEvent>> GetPendingAsync(int max = 50)
        => Task.FromResult<IReadOnlyList<AvizoroEvent>>(Saved);

    public Task DeleteAsync(Guid id) => Task.CompletedTask;

    public Task<int> CountAsync() => Task.FromResult(Saved.Count);
}

internal sealed class FakeUploader : IEventUploader
{
    private readonly CallLog _log;
    public int FlushCount { get; private set; }

    public FakeUploader(CallLog log) => _log = log;

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        FlushCount++;
        _log.Record("uploader.flush");
        return Task.CompletedTask;
    }
}

internal sealed class FakeDeviceContextProvider : IDeviceContextProvider
{
    public string Platform { get; init; } = "Android";
    public string DeviceName { get; init; } = "Pixel";
    public string Model { get; init; } = "Pixel 8";
    public string Manufacturer { get; init; } = "Google";
    public string OperatingSystem { get; init; } = "Android 15";
}

internal sealed class FakeRuntimeContextProvider : IRuntimeContextProvider
{
    private readonly Func<DeviceRuntime> _factory;
    public FakeRuntimeContextProvider(Func<DeviceRuntime>? factory = null)
        => _factory = factory ?? (() => new DeviceRuntime { BatteryLevel = 0.5 });

    public DeviceRuntime Capture() => _factory();
}

internal sealed class FakeLastScreenProvider : ILastScreenProvider
{
    private readonly byte[]? _bytes;
    public FakeLastScreenProvider(byte[]? bytes) => _bytes = bytes;
    public byte[]? GetLastScreenshot() => _bytes;
}

internal sealed class FakeScreenshotStore : IScreenshotStore
{
    public Dictionary<Guid, byte[]> Saved { get; } = new();

    public Task SaveAsync(Guid eventId, byte[] jpeg)
    {
        Saved[eventId] = jpeg;
        return Task.CompletedTask;
    }

    public Task<byte[]?> GetAsync(Guid eventId)
        => Task.FromResult(Saved.TryGetValue(eventId, out var b) ? b : null);

    public Task DeleteAsync(Guid eventId)
    {
        Saved.Remove(eventId);
        return Task.CompletedTask;
    }
}
