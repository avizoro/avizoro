using Avizoro.Core.Enums;

namespace Avizoro.Core.Models;

/// <summary>
/// One app session — a foreground period of use. Sessions are what turn raw
/// crash counts into a <em>crash-free rate</em>: "99.2% of sessions on 2.4.0 didn't
/// crash". Each state change is sent as an update of the same <see cref="SessionId"/>;
/// the server keeps the latest one (by <see cref="Sequence"/>).
/// </summary>
public class SessionInfo
{
    public Guid SessionId { get; set; } = Guid.NewGuid();

    /// <summary>Stable anonymous id of the installation, used to count affected users.</summary>
    public string DistinctId { get; set; } = string.Empty;

    public DateTime Started { get; set; } = DateTime.UtcNow;

    public SessionStatus Status { get; set; } = SessionStatus.Ok;

    /// <summary>Handled errors captured during the session (crashes are the status).</summary>
    public int Errors { get; set; }

    /// <summary>Foreground time so far, in seconds.</summary>
    public double DurationSeconds { get; set; }

    /// <summary>Monotonic per-session counter so the server can ignore stale updates.</summary>
    public long Sequence { get; set; }

    /// <summary>True only on the first update of a session.</summary>
    public bool Init { get; set; }

    public SessionInfo Clone() => (SessionInfo)MemberwiseClone();
}
