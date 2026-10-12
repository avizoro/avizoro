using Avizoro.Core.Models;

namespace Avizoro.Core.Storage;

/// <summary>
/// Durable local queue of events waiting to be delivered. Enables
/// store-and-forward: every event is persisted first, then drained to the
/// server, so nothing is lost while offline or during a crash.
/// </summary>
public interface IEventStore
{
    Task SaveAsync(AvizoroEvent avizoroEvent);

    /// <summary>Oldest-first batch of pending events, up to <paramref name="max"/>.</summary>
    Task<IReadOnlyList<AvizoroEvent>> GetPendingAsync(int max = 50);

    Task DeleteAsync(Guid id);

    Task<int> CountAsync();
}
