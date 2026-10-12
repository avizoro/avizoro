using System.Text.Json;
using IntelligenceKit.Core.Enums;
using IntelligenceKit.Core.Models;
using IntelligenceKit.Server.Contracts;
using IntelligenceKit.Server.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceKit.Server.Sessions;

public sealed record TimelineIngestResult(bool Duplicate, int Stored);

/// <summary>
/// Stores an SDK <see cref="EventType.Timeline"/> batch as <see cref="TimelineEntry"/> rows and pushes
/// them to live dashboards. Idempotent by batch id (the SDK retries a batch until it is acknowledged),
/// and bounded per batch and per session.
/// </summary>
public sealed class TimelineIngestor
{
    private const int MaxDataKeys = 20;
    private const int MaxDataValueLength = 500;

    private readonly IntelligenceDbContext _db;
    private readonly IHubContext<EventsHub> _hub;
    private readonly ILogger<TimelineIngestor> _logger;

    public TimelineIngestor(IntelligenceDbContext db, IHubContext<EventsHub> hub, ILogger<TimelineIngestor> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task<TimelineIngestResult> IngestAsync(IntelligenceEvent batch, CancellationToken ct = default)
    {
        var sessionId = batch.SessionId!.Value;
        var batchId = batch.Id == Guid.Empty ? Guid.NewGuid() : batch.Id;

        if (await _db.TimelineEntries.AnyAsync(t => t.BatchId == batchId, ct))
            return new TimelineIngestResult(true, 0);

        var existing = await _db.TimelineEntries.CountAsync(t => t.SessionId == sessionId, ct);
        var room = Math.Min(TimelineLimits.EntriesPerBatch, TimelineLimits.EntriesPerSession - existing);
        if (room <= 0)
        {
            _logger.LogWarning("Timeline of session {SessionId} is full; dropped a batch of {Count} entries.", sessionId, batch.Breadcrumbs.Count);
            return new TimelineIngestResult(false, 0);
        }

        var now = DateTime.UtcNow;
        var start = batch.TimelineSequence ?? 0;
        var entries = batch.Breadcrumbs.Take(room).Select((b, i) => new TimelineEntry
        {
            Id = Guid.NewGuid(),
            ProjectId = batch.ProjectId,
            SessionId = sessionId,
            BatchId = batchId,
            Sequence = start + i,
            Timestamp = b.Timestamp == default ? now : b.Timestamp,
            Category = Truncate(b.Category, 64),
            Message = Truncate(b.Message, TimelineLimits.MessageLength),
            Level = b.Level.ToString(),
            DataJson = SerializeData(b.Data),
            ReceivedAt = now,
        }).ToList();

        _db.TimelineEntries.AddRange(entries);

        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is not null)
        {
            session.TimelineEntries += entries.Count;
            var latest = entries.Max(e => e.Timestamp);
            if (session.LastActivity is null || latest > session.LastActivity)
                session.LastActivity = latest;
        }

        await _db.SaveChangesAsync(ct);

        // Live session view. Best-effort: the entries are already stored.
        try
        {
            await _hub.Clients.Groups("admins", $"project:{batch.ProjectId}")
                .SendAsync("timelineReceived", new TimelinePush(sessionId, batch.ProjectId, entries.Select(e => e.ToItem()).ToList()), ct);
            if (session is not null)
                await _hub.Clients.Groups("admins", $"project:{batch.ProjectId}").SendAsync("sessionUpdated", session.ToSummary(), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to push timeline of session {SessionId}.", sessionId);
        }

        return new TimelineIngestResult(false, entries.Count);
    }

    private static string? SerializeData(Dictionary<string, string>? data)
    {
        if (data is not { Count: > 0 })
            return null;

        var bounded = data.Take(MaxDataKeys).ToDictionary(kv => Truncate(kv.Key, 100), kv => Truncate(kv.Value, MaxDataValueLength));
        return JsonSerializer.Serialize(bounded);
    }

    private static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];
}
