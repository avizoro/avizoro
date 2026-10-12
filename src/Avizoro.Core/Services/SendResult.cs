namespace Avizoro.Core.Services;

/// <summary>
/// Outcome of attempting to deliver an event to the server. Lets the uploader
/// decide whether to remove the event from the offline queue or keep it.
/// </summary>
public enum SendResult
{
    /// <summary>Accepted by the server (2xx). Safe to remove from the queue.</summary>
    Delivered,

    /// <summary>Rejected for good (4xx). Retrying won't help; drop it so it can't poison the queue.</summary>
    Rejected,

    /// <summary>Network error or 5xx. Keep the event and retry later.</summary>
    TransientFailure
}
