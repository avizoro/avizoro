namespace Avizoro.Core.Storage;

/// <summary>
/// Durable local store for the "last screen" JPEG attached to an event, keyed by
/// the event's id. Kept separate from the event queue so image bytes never bloat
/// the JSON payloads; the uploader ships the blob after its event is delivered.
/// </summary>
public interface IScreenshotStore
{
    Task SaveAsync(Guid eventId, byte[] jpeg);

    Task<byte[]?> GetAsync(Guid eventId);

    Task DeleteAsync(Guid eventId);
}
