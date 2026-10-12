using Avizoro.Server.Data;
using Avizoro.Server.Ingest;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Alerts;

/// <summary>
/// Decides, at ingest time, which alert rules an event fires. For each match it
/// records a pending <see cref="AlertNotification"/> (which doubles as the cooldown
/// marker) and hands an <see cref="AlertJob"/> to the <see cref="AlertQueue"/>.
/// Ignored issues never alert.
/// </summary>
public sealed class AlertEvaluator
{
    private readonly AvizoroDbContext _db;
    private readonly AlertQueue _queue;

    public AlertEvaluator(AvizoroDbContext db, AlertQueue queue)
    {
        _db = db;
        _queue = queue;
    }

    public async Task EvaluateAsync(StoredEvent stored, Issue issue, IssueChange change, CancellationToken ct = default)
    {
        if (issue.Status == IssueStatuses.Ignored)
            return;

        var rules = await _db.AlertRules.AsNoTracking()
            .Where(r => r.Enabled && (r.ProjectId == null || r.ProjectId == issue.ProjectId))
            .ToListAsync(ct);

        if (rules.Count == 0)
            return;

        var now = DateTime.UtcNow;
        var jobs = new List<AlertJob>();

        foreach (var rule in rules)
        {
            int? windowCount = null;

            switch (rule.Trigger)
            {
                case AlertTriggers.NewIssue when change == IssueChange.New:
                case AlertTriggers.Regression when change == IssueChange.Regressed:
                    break;

                case AlertTriggers.Threshold when rule.ThresholdCount > 0 && rule.ThresholdWindowMinutes > 0:
                    var since = now.AddMinutes(-rule.ThresholdWindowMinutes);
                    var count = await _db.Events.AsNoTracking().CountAsync(e =>
                        e.ProjectId == issue.ProjectId && e.Fingerprint == issue.Fingerprint && e.ReceivedAt >= since, ct);
                    if (count < rule.ThresholdCount)
                        continue;
                    windowCount = count;
                    break;

                default:
                    continue;
            }

            if (rule.CooldownMinutes > 0)
            {
                var cooldownStart = now.AddMinutes(-rule.CooldownMinutes);
                var recentlyFired = await _db.AlertNotifications.AsNoTracking().AnyAsync(n =>
                    n.RuleId == rule.Id && n.IssueId == issue.Id && n.CreatedAt >= cooldownStart, ct);
                if (recentlyFired)
                    continue;
            }

            var notification = new AlertNotification
            {
                Id = Guid.NewGuid(),
                RuleId = rule.Id,
                RuleName = rule.Name,
                ProjectId = issue.ProjectId,
                IssueId = issue.Id,
                IssueTitle = issue.Title,
                Trigger = rule.Trigger,
                Channel = rule.Channel,
                CreatedAt = now,
            };
            _db.AlertNotifications.Add(notification);

            jobs.Add(new AlertJob(
                notification.Id, rule.Name, rule.Trigger, rule.Channel, rule.Target, rule.Secret,
                issue.ToSummary(), stored.ToSummary(),
                windowCount, rule.Trigger == AlertTriggers.Threshold ? rule.ThresholdWindowMinutes : null));
        }

        if (jobs.Count == 0)
            return;

        // Persist the pending rows before queueing, so the cooldown holds and a
        // restart can recover undelivered alerts.
        await _db.SaveChangesAsync(ct);
        foreach (var job in jobs)
            _queue.Enqueue(job);
    }
}
