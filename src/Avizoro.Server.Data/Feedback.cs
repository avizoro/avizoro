namespace Avizoro.Server.Data;

/// <summary>User-written feedback about an event (usually a crash), shown on its issue.</summary>
public class Feedback
{
    public Guid Id { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    /// <summary>The event the user is describing.</summary>
    public Guid EventId { get; set; }

    /// <summary>Issue of that event, resolved at ingest when the event is already stored.</summary>
    public Guid? IssueId { get; set; }

    public string Comments { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? UserId { get; set; }

    public DateTime CreatedAt { get; set; }
}
