namespace Avizoro.Server.Data;

/// <summary>
/// The "last screen" image attached to an event, stored as a JPEG blob keyed by
/// the event's id (one screenshot per event).
/// </summary>
public class StoredScreenshot
{
    public Guid EventId { get; set; }

    public byte[] Jpeg { get; set; } = Array.Empty<byte>();

    public string ContentType { get; set; } = "image/jpeg";

    public DateTime ReceivedAt { get; set; }
}
