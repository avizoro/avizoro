using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Alerts;

/// <summary>
/// Alert rule management and delivery history. Admin-only: a rule makes the
/// server POST to an arbitrary URL, so only the operator may define targets.
/// </summary>
public static class AlertEndpoints
{
    public static void MapAlertEndpoints(this WebApplication app, string adminPolicy)
    {
        var group = app.MapGroup("/alerts").RequireAuthorization(adminPolicy);

        group.MapGet("/rules", async (AvizoroDbContext db, string? projectId) =>
        {
            var query = db.AlertRules.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(projectId))
                query = query.Where(r => r.ProjectId == projectId || r.ProjectId == null);

            var rules = await query.OrderBy(r => r.CreatedAt).ToListAsync();
            return Results.Ok(rules.Select(ToInfo));
        });

        group.MapPost("/rules", async (UpsertAlertRuleRequest req, AvizoroDbContext db) =>
        {
            var rule = new AlertRule { Id = Guid.NewGuid(), CreatedAt = DateTime.UtcNow };
            var error = Apply(rule, req);
            if (error is not null)
                return Results.BadRequest(error);

            db.AlertRules.Add(rule);
            await db.SaveChangesAsync();
            return Results.Created($"/alerts/rules/{rule.Id}", ToInfo(rule));
        });

        group.MapPut("/rules/{id:guid}", async (Guid id, UpsertAlertRuleRequest req, AvizoroDbContext db) =>
        {
            var rule = await db.AlertRules.FirstOrDefaultAsync(r => r.Id == id);
            if (rule is null)
                return Results.NotFound();

            // Keep the existing signing secret unless a new one is supplied.
            var previousSecret = rule.Secret;
            var error = Apply(rule, req);
            if (error is not null)
                return Results.BadRequest(error);
            if (req.Secret is null)
                rule.Secret = previousSecret;

            await db.SaveChangesAsync();
            return Results.Ok(ToInfo(rule));
        });

        group.MapDelete("/rules/{id:guid}", async (Guid id, AvizoroDbContext db) =>
        {
            var deleted = await db.AlertRules.Where(r => r.Id == id).ExecuteDeleteAsync();
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Sends a synthetic alert through the rule right now and reports the
        // outcome, so a misconfigured URL/SMTP is caught at setup time.
        group.MapPost("/rules/{id:guid}/test", async (Guid id, AvizoroDbContext db, AlertSender sender) =>
        {
            var rule = await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
            if (rule is null)
                return Results.NotFound();

            var now = DateTime.UtcNow;
            var issue = new IssueSummary(
                Guid.Empty, rule.ProjectId ?? "test-project", "test", "Test alert from Avizoro",
                "AlertRules.Test", "Exception", "Error", 1, now, now, Guid.Empty, 1, 0);
            var job = new AlertJob(Guid.Empty, rule.Name, rule.Trigger, rule.Channel, rule.Target, rule.Secret,
                issue, Event: null, WindowCount: rule.ThresholdCount, WindowMinutes: rule.ThresholdWindowMinutes);

            try
            {
                await sender.SendAsync(job);
                return Results.Ok(new { success = true });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { success = false, error = ex.Message });
            }
        });

        group.MapGet("/history", async (AvizoroDbContext db, string? projectId, Guid? issueId, int skip = 0, int take = 50) =>
        {
            take = Math.Clamp(take, 1, 200);

            var query = db.AlertNotifications.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(projectId))
                query = query.Where(n => n.ProjectId == projectId);
            if (issueId is not null)
                query = query.Where(n => n.IssueId == issueId);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(n => n.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(n => new AlertNotificationInfo(
                    n.Id, n.RuleId, n.RuleName, n.ProjectId, n.IssueId, n.IssueTitle,
                    n.Trigger, n.Channel, n.CreatedAt, n.Success, n.Error, n.DeliveredAt))
                .ToListAsync();

            return Results.Ok(new PagedResult<AlertNotificationInfo>(total, skip, take, items));
        });
    }

    private static AlertRuleInfo ToInfo(AlertRule r) => new(
        r.Id, r.ProjectId, r.Name, r.Trigger, r.ThresholdCount, r.ThresholdWindowMinutes,
        r.Channel, r.Target, !string.IsNullOrEmpty(r.Secret), r.Enabled, r.CooldownMinutes, r.CreatedAt);

    /// <summary>Validates <paramref name="req"/> and copies it onto <paramref name="rule"/>. Returns an error or null.</summary>
    private static string? Apply(AlertRule rule, UpsertAlertRuleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return "name is required.";

        var trigger = AlertTriggers.Normalize(req.Trigger);
        if (trigger is null)
            return $"Unknown trigger '{req.Trigger}'. Use one of: {string.Join(", ", AlertTriggers.All)}.";

        var channel = AlertChannels.Normalize(req.Channel);
        if (channel is null)
            return $"Unknown channel '{req.Channel}'. Use one of: {string.Join(", ", AlertChannels.All)}.";

        if (string.IsNullOrWhiteSpace(req.Target))
            return "target is required (webhook URL or email recipients).";

        if (channel != AlertChannels.Email &&
            (!Uri.TryCreate(req.Target, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
            return "target must be an absolute http(s) URL for webhook channels.";

        if (trigger == AlertTriggers.Threshold && (req.ThresholdCount < 1 || req.ThresholdWindowMinutes < 1))
            return "Threshold rules need thresholdCount >= 1 and thresholdWindowMinutes >= 1.";

        if (req.CooldownMinutes < 0)
            return "cooldownMinutes must be >= 0.";

        rule.Name = req.Name.Trim();
        rule.ProjectId = string.IsNullOrWhiteSpace(req.ProjectId) ? null : req.ProjectId.Trim();
        rule.Trigger = trigger;
        rule.Channel = channel;
        rule.Target = req.Target.Trim();
        rule.Secret = string.IsNullOrEmpty(req.Secret) ? null : req.Secret;
        rule.ThresholdCount = trigger == AlertTriggers.Threshold ? req.ThresholdCount : 0;
        rule.ThresholdWindowMinutes = trigger == AlertTriggers.Threshold ? req.ThresholdWindowMinutes : 0;
        rule.Enabled = req.Enabled;
        rule.CooldownMinutes = req.CooldownMinutes;
        return null;
    }
}
