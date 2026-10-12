using System.Text.Json;
using IntelligenceKit.Core.Models;

namespace IntelligenceKit.Server.Contracts;

/// <summary>A page of results plus the total count, so clients can paginate.</summary>
public record PagedResult<T>(int Total, int Skip, int Take, IReadOnlyList<T> Items);

/// <summary>Lightweight row for the events list / tables.</summary>
public record EventSummary(
    Guid Id,
    string ProjectId,
    string ApplicationName,
    string ApplicationVersion,
    string Environment,
    string Platform,
    string DeviceName,
    string EventType,
    string? Level,
    string? UserId,
    string? ExceptionType,
    string? ExceptionMessage,
    string? Message,
    DateTime Timestamp,
    DateTime ReceivedAt,
    Guid? SessionId = null);

/// <summary>One hour of the events-per-hour time series.</summary>
public record TimeBucket(DateTime Start, int Total, int Exceptions);

/// <summary>
/// A grouped problem for the Issues view. <c>RecentCount</c>/<c>PreviousCount</c>
/// are occurrences in the last hour vs the hour before, so the client can draw a
/// trend arrow. <c>Status</c> is Unresolved | Resolved | Ignored; <c>IsRegression</c>
/// flags a resolved issue that came back.
/// </summary>
public record IssueSummary(
    Guid Id,
    string ProjectId,
    string Fingerprint,
    string Title,
    string? Culprit,
    string EventType,
    string? Level,
    long EventCount,
    DateTime FirstSeen,
    DateTime LastSeen,
    Guid LastEventId,
    int RecentCount,
    int PreviousCount,
    string Status = "Unresolved",
    bool IsRegression = false,
    string? AssignedTo = null,
    DateTime? ResolvedAt = null,
    string? ResolvedInRelease = null,
    DateTime? RegressedAt = null,
    string? FirstRelease = null,
    string? LastRelease = null,
    string? ExternalIssueUrl = null);

/// <summary>
/// Triage update for an issue (PATCH /issues/{id}). Every field is optional; only
/// the ones present are applied. <c>AssignedTo</c> = "" clears the assignee.
/// <c>ResolvedInRelease</c> only applies together with <c>Status = Resolved</c>.
/// </summary>
public record UpdateIssueRequest(string? Status = null, string? AssignedTo = null, string? ResolvedInRelease = null);

/// <summary>Per-project rollup for the projects overview.</summary>
public record ProjectSummary(
    string ProjectId,
    int EventCount,
    int ExceptionCount,
    DateTime LastEventAt);

/// <summary>Body for creating a project. ProjectKey is optional — generated when omitted.</summary>
public record CreateProjectRequest(string ProjectId, string Name, string? ProjectKey);

/// <summary>A registered project as returned by the admin API (never includes the read key).</summary>
public record ProjectInfo(
    Guid Id,
    string ProjectId,
    string ProjectKey,
    string Name,
    DateTime CreatedAt);

/// <summary>
/// Returned once when a project is created or its key is rotated — the only time
/// the plaintext <c>ReadKey</c> is ever exposed (only its hash is stored).
/// </summary>
public record ProjectCredentials(
    Guid Id,
    string ProjectId,
    string ProjectKey,
    string Name,
    DateTime CreatedAt,
    string ReadKey);

/// <summary>Full event, with the exception tree, context and trail already parsed.</summary>
public record EventDetail(
    Guid Id,
    string ProjectId,
    string ProjectKey,
    string ApplicationName,
    string ApplicationVersion,
    string Environment,
    string Release,
    string Platform,
    string DeviceName,
    string DeviceModel,
    string Manufacturer,
    string OperatingSystem,
    string? UserId,
    string EventType,
    string? Level,
    string? Message,
    ExceptionInfo? Exception,
    DeviceRuntime? DeviceRuntime,
    IReadOnlyList<Breadcrumb> Breadcrumbs,
    Dictionary<string, string>? Tags,
    Dictionary<string, JsonElement>? Data,
    bool HasScreenshot,
    DateTime Timestamp,
    DateTime ReceivedAt,
    bool Symbolicated = false,
    Guid? SessionId = null);

/// <summary>An alert rule as returned by the API. <c>HasSecret</c> hides the signing key itself.</summary>
public record AlertRuleInfo(
    Guid Id,
    string? ProjectId,
    string Name,
    string Trigger,
    int ThresholdCount,
    int ThresholdWindowMinutes,
    string Channel,
    string Target,
    bool HasSecret,
    bool Enabled,
    int CooldownMinutes,
    DateTime CreatedAt);

/// <summary>
/// Create/replace an alert rule. <c>Trigger</c>: NewIssue | Regression | Threshold
/// (Threshold also needs <c>ThresholdCount</c> and <c>ThresholdWindowMinutes</c>).
/// <c>Channel</c>: Webhook | Slack | Teams | Discord | Email; <c>Target</c> is the
/// webhook URL or comma-separated email recipients. <c>ProjectId</c> null = all projects.
/// </summary>
public record UpsertAlertRuleRequest(
    string Name,
    string Trigger,
    string Channel,
    string Target,
    string? ProjectId = null,
    int ThresholdCount = 0,
    int ThresholdWindowMinutes = 0,
    string? Secret = null,
    bool Enabled = true,
    int CooldownMinutes = 60);

/// <summary>One entry in the alert delivery history. <c>Success</c> null = still pending.</summary>
public record AlertNotificationInfo(
    Guid Id,
    Guid RuleId,
    string RuleName,
    string ProjectId,
    Guid? IssueId,
    string? IssueTitle,
    string Trigger,
    string Channel,
    DateTime CreatedAt,
    bool? Success,
    string? Error,
    DateTime? DeliveredAt);

/// <summary>
/// Crash-free rates for a window. Rates are 0–1 and null when there were no
/// sessions. <c>AbnormalSessions</c> are sessions that stopped reporting while in
/// the foreground (e.g. killed by the OS or a native crash the SDK couldn't see).
/// </summary>
public record CrashFreeStats(
    int Sessions,
    int CrashedSessions,
    int ErroredSessions,
    int AbnormalSessions,
    double? CrashFreeSessionRate,
    int Users,
    int CrashedUsers,
    double? CrashFreeUserRate,
    IReadOnlyList<CrashFreeDay> Daily);

/// <summary>One UTC day of the crash-free series.</summary>
public record CrashFreeDay(DateTime Date, int Sessions, int CrashedSessions, double? CrashFreeSessionRate);

/// <summary>
/// Health of one release over a window. <c>Adoption</c> is this release's share of
/// all sessions in the last 24 hours (0–1, null without sessions). <c>NewIssues</c>
/// counts issues first seen in this release.
/// </summary>
public record ReleaseHealth(
    string Release,
    DateTime FirstSeen,
    DateTime LastSeen,
    int Sessions,
    int CrashedSessions,
    double? CrashFreeSessionRate,
    int Users,
    double? CrashFreeUserRate,
    double? Adoption,
    int Events,
    int Exceptions,
    int NewIssues);

/// <summary>An uploaded symbol file (content omitted).</summary>
public record SymbolFileInfo(Guid Id, string Kind, string Key, string FileName, long Size, DateTime UploadedAt);

/// <summary>Outcome for one file of a symbol upload; <c>Error</c> set when it was skipped.</summary>
public record SymbolUploadResult(string FileName, string? Kind, string? Key, string? Error);

/// <summary>Latency summary for one (operation, name) — e.g. one page or one endpoint.</summary>
public record PerformanceSummary(
    string Operation,
    string Name,
    int Count,
    double AvgMs,
    double P50Ms,
    double P75Ms,
    double P95Ms,
    double FailureRate);

/// <summary>User-written feedback about an event.</summary>
public record FeedbackInfo(
    Guid Id,
    string ProjectId,
    Guid EventId,
    Guid? IssueId,
    string Comments,
    string? Name,
    string? Email,
    string? UserId,
    DateTime CreatedAt);

/// <summary>
/// Search filters for <c>GET /events</c> (and exports). Every field is optional and
/// they combine with AND. <c>Q</c> matches message, exception message and exception
/// type; <c>Tag</c> takes <c>key:value</c> and may repeat. Bound from the query
/// string on the server and rendered back to it by <see cref="ToQueryString"/>.
/// </summary>
public sealed class EventFilter
{
    public string? ProjectId { get; set; }
    public string? EventType { get; set; }
    public string? Q { get; set; }
    public string? Level { get; set; }
    public string? Release { get; set; }
    public string? Environment { get; set; }
    public string? Platform { get; set; }
    public string? UserId { get; set; }
    public string? OperatingSystem { get; set; }
    public string? DeviceModel { get; set; }
    public string[]? Tag { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    /// <summary>"a=1&amp;b=2" (no leading '?'); empty when no filter is set.</summary>
    public string ToQueryString()
    {
        var parts = new List<string>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }

        Add("projectId", ProjectId);
        Add("eventType", EventType);
        Add("q", Q);
        Add("level", Level);
        Add("release", Release);
        Add("environment", Environment);
        Add("platform", Platform);
        Add("userId", UserId);
        Add("operatingSystem", OperatingSystem);
        Add("deviceModel", DeviceModel);
        foreach (var tag in Tag ?? [])
            Add("tag", tag);
        Add("from", From?.ToUniversalTime().ToString("O"));
        Add("to", To?.ToUniversalTime().ToString("O"));
        return string.Join('&', parts);
    }
}

/// <summary>How an issue's recent events spread across one dimension (platform, OS, a tag…).</summary>
public record Distribution(string Key, int Total, IReadOnlyList<DistributionValue> Values);

/// <summary>One value of a <see cref="Distribution"/>; <c>Share</c> is 0–1.</summary>
public record DistributionValue(string Value, int Count, double Share);

/// <summary>Distributions of an issue over its latest <c>SampledEvents</c> events.</summary>
public record IssueDistributions(int SampledEvents, int AffectedUsers, IReadOnlyList<Distribution> Distributions);

/// <summary>A user (opt-in <c>SetUser</c> id) affected by an issue.</summary>
public record AffectedUser(string UserId, int Events, DateTime FirstSeen, DateTime LastSeen);

/// <summary>Everything one user ran into: issues, sessions and their latest events.</summary>
public record UserTimeline(
    string UserId,
    int TotalEvents,
    int Sessions,
    int CrashedSessions,
    IReadOnlyList<IssueSummary> Issues,
    IReadOnlyList<EventSummary> Events);

/// <summary>Which issue trackers the server is configured for.</summary>
public record IntegrationsInfo(bool GitHub, bool GitHubCreatesViaApi, bool Jira);

/// <summary>
/// Result of "create external issue". <c>Prefilled</c>: the URL opens a prefilled
/// form the user submits (GitHub without a token); <c>Created</c>: it was created now.
/// </summary>
public record ExternalIssueResult(string Url, bool Created, bool Prefilled);

/// <summary>A session as listed on the Sessions page (release-health row plus device and timeline counts).</summary>
public record SessionSummary(
    Guid Id,
    string ProjectId,
    string Status,
    bool IsAbnormal,
    DateTime Started,
    DateTime LastUpdate,
    DateTime? LastActivity,
    double DurationSeconds,
    int Errors,
    int TimelineEntries,
    string Release,
    string Environment,
    string Platform,
    string DeviceModel,
    string Manufacturer,
    string OperatingSystem,
    string? UserId,
    string DistinctId);

/// <summary>
/// One row of a session timeline: either a recorded step (<see cref="Kind"/> "entry": navigation,
/// user action, HTTP call, log, screenshot…) or an error/log event captured in the session ("event").
/// </summary>
public record TimelineItem(
    string Kind,
    Guid Id,
    DateTime Timestamp,
    long Sequence,
    string Category,
    string Message,
    string Level,
    IReadOnlyDictionary<string, string>? Data,
    Guid? EventId = null,
    string? EventType = null,
    string? ExceptionType = null,
    string? ExceptionMessage = null,
    Guid? ScreenshotId = null,
    string? EventMessage = null)
{
    public const string EntryKind = "entry";
    public const string EventKind = "event";

    private static readonly TimeSpan SameLogWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// True when <paramref name="entry"/> is the log breadcrumb recorded by the same call that captured
    /// <paramref name="evt"/> (<c>TrackLogAsync</c> and the ILogger provider produce both). The timeline
    /// keeps only the event, which links to its details.
    /// </summary>
    public static bool IsSameLog(TimelineItem entry, TimelineItem evt)
    {
        if (entry.Kind != EntryKind || entry.Category != "log" || evt.Kind != EventKind || evt.EventMessage is not { Length: > 0 } message)
            return false;

        // Timeline messages are truncated at ingest; compare the same prefix.
        var comparable = message.Length > entry.Message.Length && entry.Message.Length >= 1000 ? message[..entry.Message.Length] : message;
        return entry.Message == comparable && (entry.Timestamp - evt.Timestamp).Duration() <= SameLogWindow;
    }
}

/// <summary>A session and its merged, chronological timeline.</summary>
public record SessionTimelineResult(SessionSummary Session, IReadOnlyList<TimelineItem> Items, bool Truncated);

/// <summary>SignalR "timelineReceived" payload: new items of one session.</summary>
public record TimelinePush(Guid SessionId, string ProjectId, IReadOnlyList<TimelineItem> Items);
