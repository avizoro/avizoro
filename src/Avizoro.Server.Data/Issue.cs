namespace Avizoro.Server.Data;

/// <summary>
/// A group of events that share a fingerprint — one recurring problem. Repeated
/// occurrences increment <see cref="EventCount"/> and push <see cref="LastSeen"/>
/// forward instead of creating new rows.
/// </summary>
public class Issue
{
    public Guid Id { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Stable grouping key (see <c>EventFingerprint</c>). Unique per project.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Short label, e.g. the exception's short type name.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Where it happens, e.g. "CheckoutPage.OnPay". Null when unknown.</summary>
    public string? Culprit { get; set; }

    /// <summary>Kind of the events in this group (Exception, Log, ...).</summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>Severity of the most recent event, if any.</summary>
    public string? Level { get; set; }

    public long EventCount { get; set; }

    public DateTime FirstSeen { get; set; }

    public DateTime LastSeen { get; set; }

    /// <summary>Id of the most recently ingested event in this group.</summary>
    public Guid LastEventId { get; set; }

    /// <summary>Triage state — one of <see cref="IssueStatuses"/>.</summary>
    public string Status { get; set; } = IssueStatuses.Unresolved;

    /// <summary>When the issue was last marked resolved. Null while unresolved.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Release the fix shipped in ("resolve in release X"). Events from releases
    /// that are provably older don't reopen the issue. Null = any new event reopens it.
    /// </summary>
    public string? ResolvedInRelease { get; set; }

    /// <summary>True once a resolved issue received a new event (a regression).
    /// Cleared when the issue is resolved again.</summary>
    public bool IsRegression { get; set; }

    /// <summary>When the latest regression was detected.</summary>
    public DateTime? RegressedAt { get; set; }

    /// <summary>Free-form owner (name/email/handle). Null = unassigned.</summary>
    public string? AssignedTo { get; set; }

    /// <summary>Release of the first event — "introduced in". Null when unknown.</summary>
    public string? FirstRelease { get; set; }

    /// <summary>Release of the most recent event.</summary>
    public string? LastRelease { get; set; }

    /// <summary>Linked GitHub/Jira issue, once one was created from here.</summary>
    public string? ExternalIssueUrl { get; set; }
}

/// <summary>Allowed values for <see cref="Issue.Status"/>.</summary>
public static class IssueStatuses
{
    public const string Unresolved = "Unresolved";
    public const string Resolved = "Resolved";
    public const string Ignored = "Ignored";

    public static readonly IReadOnlyList<string> All = [Unresolved, Resolved, Ignored];

    /// <summary>Canonical casing for a user-supplied status, or null when unknown.</summary>
    public static string? Normalize(string? value)
        => All.FirstOrDefault(s => string.Equals(s, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
