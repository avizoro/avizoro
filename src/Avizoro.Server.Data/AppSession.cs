namespace Avizoro.Server.Data;

/// <summary>
/// Latest known state of one app session (release health). Upserted from the
/// SDK's session updates; the source of the crash-free sessions/users rates.
/// </summary>
public class AppSession
{
    /// <summary>The SDK-generated session id.</summary>
    public Guid Id { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    public string Release { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string DeviceModel { get; set; } = string.Empty;

    public string Manufacturer { get; set; } = string.Empty;

    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Anonymous installation id.</summary>
    public string DistinctId { get; set; } = string.Empty;

    public string? UserId { get; set; }

    public DateTime Started { get; set; }

    /// <summary>When the latest update was received.</summary>
    public DateTime LastUpdate { get; set; }

    /// <summary>Ok | Exited | Crashed (SessionStatus name).</summary>
    public string Status { get; set; } = "Ok";

    public int Errors { get; set; }

    public double DurationSeconds { get; set; }

    /// <summary>Sequence of the applied update; older/equal updates are ignored.</summary>
    public long Sequence { get; set; }

    /// <summary>Timeline entries received for this session (only with <c>EnableSessionTimeline</c>).</summary>
    public int TimelineEntries { get; set; }

    /// <summary>When the latest timeline entry happened on the device.</summary>
    public DateTime? LastActivity { get; set; }
}
