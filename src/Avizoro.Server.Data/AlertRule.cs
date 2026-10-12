namespace Avizoro.Server.Data;

/// <summary>
/// "When X happens to an issue, notify Y." Evaluated at ingest, right after the
/// event is grouped into its issue; matching rules enqueue an
/// <see cref="AlertNotification"/> that a background dispatcher delivers.
/// </summary>
public class AlertRule
{
    public Guid Id { get; set; }

    /// <summary>Project the rule watches; null = every project.</summary>
    public string? ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>One of <see cref="AlertTriggers"/>.</summary>
    public string Trigger { get; set; } = AlertTriggers.NewIssue;

    /// <summary>Threshold trigger: fire when an issue gets at least this many events…</summary>
    public int ThresholdCount { get; set; }

    /// <summary>…within this many minutes.</summary>
    public int ThresholdWindowMinutes { get; set; }

    /// <summary>One of <see cref="AlertChannels"/>.</summary>
    public string Channel { get; set; } = AlertChannels.Webhook;

    /// <summary>Webhook URL, or comma-separated recipients for email.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Generic webhook only: HMAC-SHA256 key used to sign the body.</summary>
    public string? Secret { get; set; }

    public bool Enabled { get; set; } = true;

    /// <summary>Minimum minutes between two notifications of this rule for the same issue.</summary>
    public int CooldownMinutes { get; set; } = 60;

    public DateTime CreatedAt { get; set; }
}

/// <summary>A single alert delivery (pending, sent or failed) — the alert history.</summary>
public class AlertNotification
{
    public Guid Id { get; set; }

    public Guid RuleId { get; set; }

    public string RuleName { get; set; } = string.Empty;

    public string ProjectId { get; set; } = string.Empty;

    public Guid? IssueId { get; set; }

    public string? IssueTitle { get; set; }

    public string Trigger { get; set; } = string.Empty;

    public string Channel { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Null while pending; true/false once the dispatcher tried to deliver it.</summary>
    public bool? Success { get; set; }

    public string? Error { get; set; }

    public DateTime? DeliveredAt { get; set; }
}

public static class AlertTriggers
{
    /// <summary>First event of a never-seen fingerprint.</summary>
    public const string NewIssue = "NewIssue";

    /// <summary>A resolved issue came back.</summary>
    public const string Regression = "Regression";

    /// <summary>An issue got ThresholdCount events within ThresholdWindowMinutes.</summary>
    public const string Threshold = "Threshold";

    public static readonly IReadOnlyList<string> All = [NewIssue, Regression, Threshold];

    public static string? Normalize(string? value)
        => All.FirstOrDefault(s => string.Equals(s, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}

public static class AlertChannels
{
    public const string Webhook = "Webhook";
    public const string Slack = "Slack";
    public const string Teams = "Teams";
    public const string Discord = "Discord";
    public const string Email = "Email";

    public static readonly IReadOnlyList<string> All = [Webhook, Slack, Teams, Discord, Email];

    public static string? Normalize(string? value)
        => All.FirstOrDefault(s => string.Equals(s, value?.Trim(), StringComparison.OrdinalIgnoreCase));
}
