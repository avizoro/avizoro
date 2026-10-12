namespace IntelligenceKit.Server.Data;

/// <summary>
/// Persisted form of an incoming event. Scalar columns are kept for quick
/// filtering/listing; the full exception tree and the free-form data bag are
/// stored as JSON so the model can evolve without schema churn.
/// </summary>
public class StoredEvent
{
    public Guid Id { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    public string ProjectKey { get; set; } = string.Empty;

    public string ApplicationName { get; set; } = string.Empty;

    public string ApplicationVersion { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    public string DeviceModel { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Deployment environment, e.g. "production", "staging".</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>Release/build identifier.</summary>
    public string Release { get; set; } = string.Empty;

    /// <summary>Opt-in user identifier, if provided.</summary>
    public string? UserId { get; set; }

    /// <summary>The SDK session the event happened in (places it on the session timeline).</summary>
    public Guid? SessionId { get; set; }

    public string EventType { get; set; } = string.Empty;

    /// <summary>Grouping key tying this event to its <see cref="Issue"/>.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Severity for log events, e.g. "Error". Null for non-log events.</summary>
    public string? Level { get; set; }

    /// <summary>Message for log events.</summary>
    public string? Message { get; set; }

    // Denormalized top-level exception for listing/filtering without parsing JSON.
    public string? ExceptionType { get; set; }

    public string? ExceptionMessage { get; set; }

    // Full ExceptionInfo tree (incl. inner exceptions) as JSON.
    public string? ExceptionJson { get; set; }

    // Runtime snapshot (memory/battery/network/screen) as JSON.
    public string? DeviceRuntimeJson { get; set; }

    // Breadcrumb trail as JSON.
    public string? BreadcrumbsJson { get; set; }

    // Indexable tags as JSON.
    public string? TagsJson { get; set; }

    // Free-form Data dictionary as JSON.
    public string? DataJson { get; set; }

    /// <summary>When the event happened on the device.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>When the server received/stored it.</summary>
    public DateTime ReceivedAt { get; set; }
}
