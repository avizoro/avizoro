using System.Text.Json;
using Avizoro.Core.Models;

namespace Avizoro.Core.Storage;

/// <summary>
/// Dependency-free durable queue: one JSON file per event in a directory, named
/// <c>{ticks}_{id}.json</c> so a directory listing is already oldest-first. Used by
/// the desktop/server SDKs (no native SQLite needed). Writes go to a temp file
/// and are then renamed, so a crash mid-write never leaves a half-written event.
/// </summary>
public sealed class FileEventStore : IEventStore
{
    private readonly string _directory;

    public FileEventStore(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public async Task SaveAsync(AvizoroEvent avizoroEvent)
    {
        await DeleteAsync(avizoroEvent.Id); // INSERT OR REPLACE semantics

        var name = $"{avizoroEvent.Timestamp.Ticks:D20}_{avizoroEvent.Id:N}.json";
        var temp = Path.Combine(_directory, name + ".tmp");
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(avizoroEvent)).ConfigureAwait(false);
        File.Move(temp, Path.Combine(_directory, name), overwrite: true);
    }

    public async Task<IReadOnlyList<AvizoroEvent>> GetPendingAsync(int max = 50)
    {
        var result = new List<AvizoroEvent>();
        foreach (var file in Files().Take(max))
        {
            try
            {
                var e = JsonSerializer.Deserialize<AvizoroEvent>(await File.ReadAllTextAsync(file).ConfigureAwait(false));
                if (e is not null)
                    result.Add(e);
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                // Corrupt or vanished entry: drop it so the queue can't get stuck.
                TryDelete(file);
            }
        }
        return result;
    }

    public Task DeleteAsync(Guid id)
    {
        foreach (var file in Directory.EnumerateFiles(_directory, $"*_{id:N}.json"))
            TryDelete(file);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync() => Task.FromResult(Files().Count());

    private IEnumerable<string> Files()
        => Directory.EnumerateFiles(_directory, "*.json").Order(StringComparer.Ordinal);

    private static void TryDelete(string file)
    {
        try { File.Delete(file); } catch (IOException) { }
    }
}

/// <summary>
/// Non-durable queue for hosts without a writable file system (e.g. Blazor
/// WebAssembly): store-and-forward still smooths over transient network errors
/// while the app runs, but pending events are lost when it closes.
/// </summary>
public sealed class InMemoryEventStore : IEventStore
{
    private readonly List<AvizoroEvent> _events = new();
    private readonly int _capacity;

    public InMemoryEventStore(int capacity = 500) => _capacity = Math.Max(1, capacity);

    public Task SaveAsync(AvizoroEvent avizoroEvent)
    {
        lock (_events)
        {
            _events.RemoveAll(e => e.Id == avizoroEvent.Id);
            _events.Add(avizoroEvent);
            if (_events.Count > _capacity)
                _events.RemoveAt(0); // drop the oldest rather than grow unbounded
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvizoroEvent>> GetPendingAsync(int max = 50)
    {
        lock (_events)
            return Task.FromResult<IReadOnlyList<AvizoroEvent>>(_events.OrderBy(e => e.Timestamp).Take(max).ToList());
    }

    public Task DeleteAsync(Guid id)
    {
        lock (_events)
            _events.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync()
    {
        lock (_events)
            return Task.FromResult(_events.Count);
    }
}

/// <summary>Screenshot store for hosts that don't capture screens.</summary>
public sealed class NullScreenshotStore : IScreenshotStore
{
    public Task SaveAsync(Guid eventId, byte[] jpeg) => Task.CompletedTask;

    public Task<byte[]?> GetAsync(Guid eventId) => Task.FromResult<byte[]?>(null);

    public Task DeleteAsync(Guid eventId) => Task.CompletedTask;
}
