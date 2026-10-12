using System.Security.Claims;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Search;

/// <summary>
/// User-centric reads, built on the opt-in <c>SetUser</c> id: who an issue affects,
/// and everything one user ran into.
/// </summary>
public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
        app.MapGet("/issues/{id:guid}/users", async (Guid id, AvizoroDbContext db, ClaimsPrincipal user, int take = 100) =>
        {
            take = Math.Clamp(take, 1, 500);
            var issue = await db.Issues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            var scope = user.ProjectScope();
            if (issue is null || (scope is not null && issue.ProjectId != scope))
                return Results.NotFound();

            // Grouped in memory (provider-neutral); bounded to the latest 10k events.
            var rows = await db.Events.AsNoTracking()
                .Where(e => e.ProjectId == issue.ProjectId && e.Fingerprint == issue.Fingerprint && e.UserId != null)
                .OrderByDescending(e => e.ReceivedAt)
                .Take(10_000)
                .Select(e => new { e.UserId, e.ReceivedAt })
                .ToListAsync();

            var users = rows
                .GroupBy(r => r.UserId!)
                .Select(g => new AffectedUser(g.Key, g.Count(), g.Min(r => r.ReceivedAt), g.Max(r => r.ReceivedAt)))
                .OrderByDescending(u => u.LastSeen)
                .Take(take)
                .ToList();

            return Results.Ok(users);
        }).RequireAuthorization();

        app.MapGet("/users/{userId}", async (string userId, AvizoroDbContext db, ClaimsPrincipal user, string? projectId, int take = 100) =>
        {
            take = Math.Clamp(take, 1, 500);
            var projectFilter = user.ProjectScope() ?? projectId;

            var events = db.Events.AsNoTracking().Where(e => e.UserId == userId);
            var sessions = db.Sessions.AsNoTracking().Where(s => s.UserId == userId);
            if (!string.IsNullOrWhiteSpace(projectFilter))
            {
                events = events.Where(e => e.ProjectId == projectFilter);
                sessions = sessions.Where(s => s.ProjectId == projectFilter);
            }

            var timeline = await events
                .OrderByDescending(e => e.ReceivedAt)
                .Take(take)
                .Select(e => new EventSummary(
                    e.Id, e.ProjectId, e.ApplicationName, e.ApplicationVersion,
                    e.Environment, e.Platform, e.DeviceName, e.EventType,
                    e.Level, e.UserId,
                    e.ExceptionType, e.ExceptionMessage, e.Message, e.Timestamp, e.ReceivedAt, e.SessionId))
                .ToListAsync();

            var totalEvents = await events.CountAsync();

            // Issues this user hit: fingerprints of their events → issues.
            var fingerprints = await events.Select(e => new { e.ProjectId, e.Fingerprint }).Distinct().Take(500).ToListAsync();
            var fpKeys = fingerprints.Select(f => f.Fingerprint).ToList();
            var issues = (await db.Issues.AsNoTracking().Where(i => fpKeys.Contains(i.Fingerprint)).ToListAsync())
                .Where(i => fingerprints.Any(f => f.ProjectId == i.ProjectId && f.Fingerprint == i.Fingerprint))
                .OrderByDescending(i => i.LastSeen)
                .Select(i => i.ToSummary())
                .ToList();

            var sessionStatuses = await sessions.Select(s => s.Status).ToListAsync();

            return Results.Ok(new UserTimeline(
                userId, totalEvents, sessionStatuses.Count, sessionStatuses.Count(s => s == "Crashed"),
                issues, timeline));
        }).RequireAuthorization();
    }
}
