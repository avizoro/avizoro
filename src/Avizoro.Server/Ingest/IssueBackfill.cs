using System.Text.Json;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Ingest;

public sealed record BackfillResult(int EventsGrouped, int IssuesCreated, int IssuesUpdated);

/// <summary>
/// Groups events stored before issue grouping existed (empty <see cref="StoredEvent.Fingerprint"/>)
/// into issues, in oldest-first batches. Idempotent: once an event has a
/// fingerprint it's never touched again, so re-running only picks up leftovers.
/// Existing issues keep their triage state; only counts/timestamps/releases grow.
/// </summary>
public sealed class IssueBackfill
{
    private const int BatchSize = 500;

    private readonly AvizoroDbContext _db;

    public IssueBackfill(AvizoroDbContext db) => _db = db;

    public async Task<BackfillResult> RunAsync(CancellationToken ct = default)
    {
        int grouped = 0, created = 0, updated = 0;

        while (true)
        {
            var batch = await _db.Events
                .Where(e => e.Fingerprint == "")
                .OrderBy(e => e.ReceivedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (batch.Count == 0)
                break;

            var computed = batch.Select(e => (Event: e, Result: EventFingerprint.Compute(Rehydrate(e)))).ToList();
            foreach (var (stored, result) in computed)
                stored.Fingerprint = result.Fingerprint;

            foreach (var group in computed.GroupBy(c => (c.Event.ProjectId, c.Result.Fingerprint)))
            {
                var events = group.Select(g => g.Event).OrderBy(e => e.ReceivedAt).ToList();
                var first = events[0];
                var last = events[^1];
                var sample = group.Last().Result;

                var issue = await _db.Issues.FirstOrDefaultAsync(
                    i => i.ProjectId == group.Key.ProjectId && i.Fingerprint == group.Key.Fingerprint, ct);

                if (issue is null)
                {
                    _db.Issues.Add(new Issue
                    {
                        Id = Guid.NewGuid(),
                        ProjectId = group.Key.ProjectId,
                        Fingerprint = group.Key.Fingerprint,
                        Title = sample.Title,
                        Culprit = sample.Culprit,
                        EventType = last.EventType,
                        Level = last.Level,
                        EventCount = events.Count,
                        FirstSeen = first.ReceivedAt,
                        LastSeen = last.ReceivedAt,
                        LastEventId = last.Id,
                        FirstRelease = NullIfEmpty(first.Release),
                        LastRelease = NullIfEmpty(last.Release),
                    });
                    created++;
                }
                else
                {
                    issue.EventCount += events.Count;
                    if (first.ReceivedAt < issue.FirstSeen)
                    {
                        issue.FirstSeen = first.ReceivedAt;
                        issue.FirstRelease = NullIfEmpty(first.Release) ?? issue.FirstRelease;
                    }
                    if (last.ReceivedAt > issue.LastSeen)
                    {
                        issue.LastSeen = last.ReceivedAt;
                        issue.LastEventId = last.Id;
                        issue.LastRelease = NullIfEmpty(last.Release) ?? issue.LastRelease;
                    }
                    updated++;
                }
            }

            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
            grouped += batch.Count;
        }

        return new BackfillResult(grouped, created, updated);
    }

    private static AvizoroEvent Rehydrate(StoredEvent e) => new()
    {
        ProjectId = e.ProjectId,
        EventType = Enum.TryParse<EventType>(e.EventType, out var type) ? type : EventType.Unknown,
        Message = e.Message,
        Release = e.Release,
        Exception = e.ExceptionJson is null
            ? (e.ExceptionType is null ? null : new ExceptionInfo { Type = e.ExceptionType, Message = e.ExceptionMessage ?? "" })
            : JsonSerializer.Deserialize<ExceptionInfo>(e.ExceptionJson)
    };

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
