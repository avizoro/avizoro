using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Retention;

/// <summary>Outcome of a single retention sweep.</summary>
public sealed record RetentionResult(
    bool Enabled, DateTime Cutoff, int Events, int Screenshots, int Issues)
{
    public int Total => Events + Screenshots + Issues;
}

/// <summary>
/// Deletes data older than the configured retention window so a self-hosted
/// database doesn't grow without bound. Config (read at call time so it can be
/// changed without a rebuild):
/// <list type="bullet">
///   <item><c>Retention:Enabled</c> — off by default; opt in to enable pruning.</item>
///   <item><c>Retention:Days</c> — age threshold (default 90).</item>
/// </list>
/// The delete is scoped by timestamp: events and screenshots by <c>ReceivedAt</c>,
/// issues by <c>LastSeen</c> (an issue with no recent activity is stale). Uses EF
/// Core <c>ExecuteDeleteAsync</c> (a single set-based DELETE per table, translated
/// for all three providers).
/// </summary>
public sealed class RetentionSweeper
{
    private readonly AvizoroDbContext _db;
    private readonly IConfiguration _config;

    public RetentionSweeper(AvizoroDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<RetentionResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var enabled = _config.GetValue("Retention:Enabled", false);
        var days = _config.GetValue("Retention:Days", 90);

        // Disabled, or a non-positive window (which would delete everything) is
        // treated as "do nothing" — retention must never be a foot-gun.
        if (!enabled || days <= 0)
            return new RetentionResult(false, DateTime.UtcNow, 0, 0, 0);

        var cutoff = DateTime.UtcNow.AddDays(-days);

        var events = await _db.Events
            .Where(e => e.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var screenshots = await _db.Screenshots
            .Where(s => s.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var issues = await _db.Issues
            .Where(i => i.LastSeen < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.Feedback
            .Where(f => f.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.Spans
            .Where(s => s.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.Sessions
            .Where(s => s.LastUpdate < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        await _db.TimelineEntries
            .Where(t => t.ReceivedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        // Alert history ages out with the data it refers to (not counted in the result).
        await _db.AlertNotifications
            .Where(n => n.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        return new RetentionResult(true, cutoff, events, screenshots, issues);
    }
}
