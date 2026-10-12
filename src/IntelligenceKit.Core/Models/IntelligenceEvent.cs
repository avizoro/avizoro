using IntelligenceKit.Core.Enums;

namespace IntelligenceKit.Core.Models;

public class IntelligenceEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string ProjectId { get; set; } = string.Empty;

    public string ApplicationName { get; set; } = string.Empty;

    public string ApplicationVersion { get; set; } = string.Empty;

    /// <summary>Deployment environment, e.g. "production", "staging".</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Release/build identifier. Defaults to the application version.</summary>
    public string Release { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string DeviceModel { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Optional, opt-in identifier for the affected user (anonymous by default).</summary>
    public string? UserId { get; set; }

    /// <summary>Severity for log events. Null for non-log events.</summary>
    public SeverityLevel? Level { get; set; }

    /// <summary>Human-readable message for log events.</summary>
    public string? Message { get; set; }

    /// <summary>
    /// Typed exception payload. Null for non-exception events.
    /// </summary>
    public ExceptionInfo? Exception { get; set; }

    public EventType EventType { get; set; } = Enums.EventType.Unknown;

    /// <summary>Runtime snapshot (memory/battery/network/screen) at capture time.</summary>
    public DeviceRuntime? DeviceRuntime { get; set; }

    /// <summary>Trail of activity leading up to this event.</summary>
    public List<Breadcrumb> Breadcrumbs { get; set; } = new();

    /// <summary>Free-form indexable labels for filtering/search.</summary>
    public Dictionary<string, string> Tags { get; set; } = new();

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>
    /// Optional custom grouping. When set, events with the same values land in the
    /// same issue regardless of their stack trace. Use <see cref="Diagnostics.EventFingerprint.DefaultToken"/>
    /// ("{{ default }}") as one of the values to refine the default grouping instead
    /// of replacing it, e.g. <c>["{{ default }}", tenantId]</c>.
    /// </summary>
    public List<string>? Fingerprint { get; set; }

    /// <summary>Session state. Only set on <see cref="EventType.Session"/> events.</summary>
    public SessionInfo? Session { get; set; }

    /// <summary>Batched timings. Only set on <see cref="EventType.Performance"/> events sent by the SDK.</summary>
    public List<PerformanceSpan>? Spans { get; set; }

    /// <summary>Only set on <see cref="EventType.Feedback"/> events.</summary>
    public UserFeedback? Feedback { get; set; }

    /// <summary>
    /// The session this event happened in, when session tracking is on. Lets the server
    /// place errors and logs on the session timeline.
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>
    /// Only set on <see cref="EventType.Timeline"/> events: the per-session sequence number of
    /// the first entry in <see cref="Breadcrumbs"/> (the rest follow contiguously).
    /// </summary>
    public long? TimelineSequence { get; set; }
}
