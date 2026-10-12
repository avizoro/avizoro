namespace IntelligenceKit.Server.Data;

/// <summary>
/// One step of a session timeline (a navigation, user action, HTTP call, log line or
/// screenshot), from the SDK's <c>EnableSessionTimeline</c> batches. Errors are not
/// copied here: they stay in <see cref="StoredEvent"/> and are merged in by session id.
/// </summary>
public class TimelineEntry
{
    public Guid Id { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    public Guid SessionId { get; set; }

    /// <summary>
    /// The SDK batch (event id) this entry arrived in. Makes ingest idempotent, and a
    /// screenshot entry's image is stored under it (<see cref="StoredScreenshot.EventId"/>).
    /// </summary>
    public Guid BatchId { get; set; }

    /// <summary>Per-session order assigned by the SDK; breaks ties between equal timestamps.</summary>
    public long Sequence { get; set; }

    /// <summary>When it happened on the device.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>Breadcrumb category: navigation, user, http, log, system, screenshot, custom…</summary>
    public string Category { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>SeverityLevel name.</summary>
    public string Level { get; set; } = "Information";

    /// <summary>Breadcrumb data (status code, duration, screen…) as JSON.</summary>
    public string? DataJson { get; set; }

    public DateTime ReceivedAt { get; set; }
}

/// <summary>Bounds applied at ingest so one chatty client can't bloat the database.</summary>
public static class TimelineLimits
{
    public const int MessageLength = 1000;

    /// <summary>Entries accepted per SDK batch (the SDK sends up to 50).</summary>
    public const int EntriesPerBatch = 200;

    /// <summary>Entries kept per session; the SDK stops at 1000 by default.</summary>
    public const int EntriesPerSession = 5000;
}
