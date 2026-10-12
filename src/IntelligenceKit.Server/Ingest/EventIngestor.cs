using System.Text.Json;
using IntelligenceKit.Core.Diagnostics;
using IntelligenceKit.Core.Models;
using IntelligenceKit.Server.Alerts;
using IntelligenceKit.Server.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceKit.Server.Ingest;

/// <summary>What happened to the event's issue during ingest.</summary>
public enum IssueChange
{
    /// <summary>The event joined an issue that was already open (or ignored).</summary>
    Existing,

    /// <summary>First sighting of this fingerprint — a brand-new issue.</summary>
    New,

    /// <summary>A resolved issue received a new event and was reopened.</summary>
    Regressed
}

public sealed record IngestResult(bool Duplicate, StoredEvent? Event, Issue? Issue, IssueChange Change);

/// <summary>
/// The ingest pipeline behind <c>POST /events</c>: persist the event, group it into
/// its issue (creating, bumping or reopening it) and push both to live dashboards.
/// Kept out of Program.cs so follow-up stages (alerts, sessions) have one place to
/// hook into.
/// </summary>
public sealed class EventIngestor
{
    private readonly IntelligenceDbContext _db;
    private readonly IHubContext<EventsHub> _hub;
    private readonly AlertEvaluator _alerts;
    private readonly ILogger<EventIngestor> _logger;

    public EventIngestor(IntelligenceDbContext db, IHubContext<EventsHub> hub, AlertEvaluator alerts, ILogger<EventIngestor> logger)
    {
        _db = db;
        _hub = hub;
        _alerts = alerts;
        _logger = logger;
    }

    public async Task<IngestResult> IngestAsync(IntelligenceEvent intelligenceEvent, string projectKey, CancellationToken ct = default)
    {
        var eventId = intelligenceEvent.Id == Guid.Empty ? Guid.NewGuid() : intelligenceEvent.Id;

        // Idempotent ingest: a client may re-send an already-delivered event while
        // retrying its screenshot upload. Treat a duplicate as success, don't insert
        // a second row, and don't re-broadcast.
        if (await _db.Events.AnyAsync(e => e.Id == eventId, ct))
            return new IngestResult(true, null, null, IssueChange.Existing);

        var fingerprint = EventFingerprint.Compute(intelligenceEvent);
        var stored = ToStored(intelligenceEvent, eventId, fingerprint.Fingerprint, projectKey);

        _db.Events.Add(stored);
        await _db.SaveChangesAsync(ct);

        // Push the new event to any live dashboards. Only admins and dashboards
        // scoped to this project receive the push.
        await _hub.Clients.Groups("admins", $"project:{stored.ProjectId}")
            .SendAsync("eventReceived", stored.ToSummary(), ct);

        var (issue, change) = await UpsertIssueAsync(stored, fingerprint, intelligenceEvent.Release, ct);

        // Push the updated issue (trend is recomputed on read).
        await _hub.Clients.Groups("admins", $"project:{issue.ProjectId}")
            .SendAsync("issueUpserted", issue.ToSummary(), ct);

        // Alerting is best-effort: the event is already stored, so a rule problem
        // must never turn a successful ingest into an error.
        try
        {
            await _alerts.EvaluateAsync(stored, issue, change, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Alert evaluation failed for event {EventId}.", stored.Id);
        }

        return new IngestResult(false, stored, issue, change);
    }

    /// <summary>
    /// Groups the event into its issue: create it on first sighting, otherwise bump
    /// the count and move LastSeen forward. A resolved issue that sees a new event
    /// is reopened and flagged as a regression — unless the fix was pinned to a
    /// release and the event provably comes from an older one.
    /// </summary>
    private async Task<(Issue Issue, IssueChange Change)> UpsertIssueAsync(
        StoredEvent stored, EventFingerprint.Result fingerprint, string? release, CancellationToken ct)
    {
        var issue = await _db.Issues
            .FirstOrDefaultAsync(i => i.ProjectId == stored.ProjectId && i.Fingerprint == stored.Fingerprint, ct);

        var change = IssueChange.Existing;

        if (issue is null)
        {
            issue = new Issue
            {
                Id = Guid.NewGuid(),
                ProjectId = stored.ProjectId,
                Fingerprint = stored.Fingerprint,
                Title = fingerprint.Title,
                Culprit = fingerprint.Culprit,
                EventType = stored.EventType,
                Level = stored.Level,
                EventCount = 1,
                FirstSeen = stored.ReceivedAt,
                LastSeen = stored.ReceivedAt,
                LastEventId = stored.Id,
                Status = IssueStatuses.Unresolved,
                FirstRelease = NullIfEmpty(release),
                LastRelease = NullIfEmpty(release),
            };
            _db.Issues.Add(issue);
            change = IssueChange.New;
        }
        else
        {
            issue.EventCount += 1;
            issue.LastSeen = stored.ReceivedAt;
            issue.LastEventId = stored.Id;
            issue.Level = stored.Level;
            issue.Title = fingerprint.Title;
            issue.Culprit = fingerprint.Culprit;
            issue.LastRelease = NullIfEmpty(release) ?? issue.LastRelease;
            issue.FirstRelease ??= NullIfEmpty(release);

            if (issue.Status == IssueStatuses.Resolved && Regresses(issue.ResolvedInRelease, release))
            {
                issue.Status = IssueStatuses.Unresolved;
                issue.IsRegression = true;
                issue.RegressedAt = stored.ReceivedAt;
                issue.ResolvedAt = null;
                change = IssueChange.Regressed;
            }
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (change == IssueChange.New)
        {
            // Two first-sightings raced on the unique (project, fingerprint) index;
            // the other request created the issue. Fold this event into it.
            _db.Entry(issue).State = EntityState.Detached;
            issue = await _db.Issues.FirstAsync(
                i => i.ProjectId == stored.ProjectId && i.Fingerprint == stored.Fingerprint, ct);
            issue.EventCount += 1;
            issue.LastSeen = stored.ReceivedAt;
            issue.LastEventId = stored.Id;
            await _db.SaveChangesAsync(ct);
            change = IssueChange.Existing;
        }

        return (issue, change);
    }

    /// <summary>
    /// Whether an event from <paramref name="eventRelease"/> reopens an issue resolved
    /// in <paramref name="resolvedInRelease"/>. Conservative: only an event from a
    /// release that parses as a strictly older version is ignored — anything we
    /// can't order counts as a regression, because missing one is worse.
    /// </summary>
    public static bool Regresses(string? resolvedInRelease, string? eventRelease)
    {
        if (string.IsNullOrWhiteSpace(resolvedInRelease))
            return true;

        if (TryParseVersion(resolvedInRelease, out var fixedIn) &&
            TryParseVersion(eventRelease, out var seenIn))
            return seenIn >= fixedIn;

        return true;
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Tolerate "v1.2.3", "1.2.3-beta" and "1.2.3+42": compare the numeric core.
        var core = value.Trim().TrimStart('v', 'V');
        var cut = core.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0)
            core = core[..cut];

        if (!core.Contains('.'))
            core += ".0";

        return Version.TryParse(core, out version!);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static StoredEvent ToStored(IntelligenceEvent e, Guid id, string fingerprint, string projectKey) => new()
    {
        Id = id,
        Fingerprint = fingerprint,
        ProjectId = e.ProjectId,
        ProjectKey = projectKey,
        ApplicationName = e.ApplicationName,
        ApplicationVersion = e.ApplicationVersion,
        Platform = e.Platform,
        DeviceName = e.DeviceName,
        DeviceModel = e.DeviceModel,
        Manufacturer = e.Manufacturer,
        OperatingSystem = e.OperatingSystem,
        Environment = e.Environment,
        Release = e.Release,
        UserId = e.UserId,
        SessionId = e.SessionId,
        EventType = e.EventType.ToString(),
        Level = e.Level?.ToString(),
        Message = e.Message,
        ExceptionType = e.Exception?.Type,
        ExceptionMessage = e.Exception?.Message,
        ExceptionJson = e.Exception is null ? null : JsonSerializer.Serialize(e.Exception),
        DeviceRuntimeJson = e.DeviceRuntime is null ? null : JsonSerializer.Serialize(e.DeviceRuntime),
        BreadcrumbsJson = e.Breadcrumbs.Count == 0 ? null : JsonSerializer.Serialize(e.Breadcrumbs),
        TagsJson = e.Tags.Count == 0 ? null : JsonSerializer.Serialize(e.Tags),
        DataJson = e.Data.Count == 0 ? null : JsonSerializer.Serialize(e.Data),
        Timestamp = e.Timestamp,
        ReceivedAt = DateTime.UtcNow
    };
}
