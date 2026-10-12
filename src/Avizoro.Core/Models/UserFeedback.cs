namespace Avizoro.Core.Models;

/// <summary>
/// What the user says happened ("I tapped Pay and it closed"), attached to a
/// captured event — typically the crash from the previous session. Written by the
/// user on purpose, so it is sent as-is (not PII-scrubbed).
/// </summary>
public class UserFeedback
{
    /// <summary>The event this feedback is about (see <c>IAvizoro.LastEventId</c>).</summary>
    public Guid EventId { get; set; }

    public string Comments { get; set; } = string.Empty;

    /// <summary>Optional, only if the user chose to give it.</summary>
    public string? Name { get; set; }

    /// <summary>Optional, only if the user chose to give it.</summary>
    public string? Email { get; set; }
}
