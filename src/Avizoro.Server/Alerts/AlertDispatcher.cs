using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Alerts;

/// <summary>
/// Background consumer of the <see cref="AlertQueue"/>: delivers each alert via
/// <see cref="AlertSender"/> and records the outcome on its
/// <see cref="AlertNotification"/> row. On startup it re-queues notifications left
/// pending by a previous run (e.g. the server restarted mid-delivery).
/// </summary>
public sealed class AlertDispatcher : BackgroundService
{
    private readonly AlertQueue _queue;
    private readonly AlertSender _sender;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AlertDispatcher> _logger;
    private readonly Telemetry.ServerMetrics _metrics;

    public AlertDispatcher(AlertQueue queue, AlertSender sender, IServiceScopeFactory scopes, ILogger<AlertDispatcher> logger,
        Telemetry.ServerMetrics metrics)
    {
        _queue = queue;
        _sender = sender;
        _scopes = scopes;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RequeuePendingAsync(stoppingToken);
        }
        catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not recover pending alerts.");
        }

        try
        {
            await foreach (var job in _queue.ReadAllAsync(stoppingToken))
                await DeliverAsync(job, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    private async Task DeliverAsync(AlertJob job, CancellationToken ct)
    {
        string? error = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await _sender.SendAsync(job, timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            error = ex.Message;
            _logger.LogWarning(ex, "Alert '{Rule}' via {Channel} failed.", job.RuleName, job.Channel);
        }

        _metrics.AlertDelivered(job.Channel, error is null);

        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AvizoroDbContext>();
            await db.AlertNotifications
                .Where(n => n.Id == job.NotificationId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Success, error == null)
                    .SetProperty(n => n.Error, error)
                    .SetProperty(n => n.DeliveredAt, DateTime.UtcNow), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not record alert delivery {Id}.", job.NotificationId);
        }
    }

    private async Task RequeuePendingAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AvizoroDbContext>();

        var pending = await db.AlertNotifications.AsNoTracking()
            .Where(n => n.Success == null && n.IssueId != null)
            .ToListAsync(ct);

        foreach (var n in pending)
        {
            var rule = await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == n.RuleId, ct);
            var issue = await db.Issues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == n.IssueId, ct);
            if (rule is null || issue is null)
            {
                await db.AlertNotifications.Where(x => x.Id == n.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Success, false)
                    .SetProperty(x => x.Error, "Rule or issue no longer exists."), ct);
                continue;
            }

            _queue.Enqueue(new AlertJob(n.Id, rule.Name, n.Trigger, rule.Channel, rule.Target, rule.Secret,
                issue.ToSummary(), Event: null));
        }
    }
}
