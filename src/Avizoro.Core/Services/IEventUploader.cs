namespace Avizoro.Core.Services;

/// <summary>
/// Drains the offline event store to the server. Safe to call often
/// (on track, on startup, on connectivity restored); concurrent calls collapse
/// into a single drain.
/// </summary>
public interface IEventUploader
{
    Task FlushAsync(CancellationToken cancellationToken = default);
}
