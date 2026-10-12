using System.Security.Claims;
using IntelligenceKit.Server.Auth;
using IntelligenceKit.Server.Contracts;
using IntelligenceKit.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceKit.Server.Sessions;

/// <summary>Session list, session detail and the merged session timeline.</summary>
public static class SessionTimelineEndpoints
{
    /// <summary>Most timeline entries returned for one session.</summary>
    public const int MaxTimelineItems = 5000;

    public static void MapSessionTimelineEndpoints(this WebApplication app)
    {
        // Newest first. status: Ok (still running) | Exited | Crashed. withTimeline=true keeps
        // only sessions that recorded a timeline.
        app.MapGet("/sessions", async (IntelligenceDbContext db, ClaimsPrincipal user,
            string? projectId, string? environment, string? release, string? status, string? userId,
            bool withTimeline = false, int skip = 0, int take = 50) =>
        {
            take = Math.Clamp(take, 1, 200);
            skip = Math.Max(0, skip);

            var query = SessionEndpoints.Query(db, user, projectId, environment, release);
            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(s => s.Status == status);
            if (!string.IsNullOrWhiteSpace(userId))
                query = query.Where(s => s.UserId == userId);
            if (withTimeline)
                query = query.Where(s => s.TimelineEntries > 0);

            var total = await query.CountAsync();
            var rows = await query.OrderByDescending(s => s.Started).Skip(skip).Take(take).ToListAsync();

            return Results.Ok(new PagedResult<SessionSummary>(total, skip, take, rows.Select(s => s.ToSummary()).ToList()));
        }).RequireAuthorization();

        app.MapGet("/sessions/{id:guid}", async (Guid id, IntelligenceDbContext db, ClaimsPrincipal user) =>
        {
            var session = await FindAsync(db, user, id);
            return session is null ? Results.NotFound() : Results.Ok(session.ToSummary());
        }).RequireAuthorization();

        // Recorded steps and the session's errors/logs, in the order they happened on the device.
        app.MapGet("/sessions/{id:guid}/timeline", async (Guid id, IntelligenceDbContext db, ClaimsPrincipal user) =>
        {
            var session = await FindAsync(db, user, id);
            if (session is null)
                return Results.NotFound();

            var entries = await db.TimelineEntries.AsNoTracking()
                .Where(t => t.SessionId == id)
                .OrderBy(t => t.Sequence)
                .Take(MaxTimelineItems + 1)
                .ToListAsync();

            var events = await db.Events.AsNoTracking()
                .Where(e => e.SessionId == id && e.ProjectId == session.ProjectId)
                .OrderBy(e => e.Timestamp)
                .Take(MaxTimelineItems)
                .Select(e => new { e.Id, e.Timestamp, e.EventType, e.Level, e.Message, e.ExceptionType, e.ExceptionMessage })
                .ToListAsync();

            var truncated = entries.Count > MaxTimelineItems;
            var eventItems = events
                .Select(e => TimelineMapping.EventItem(e.Id, e.Timestamp, e.EventType, e.Level, e.Message, e.ExceptionType, e.ExceptionMessage))
                .ToList();
            var items = entries.Take(MaxTimelineItems).Select(e => e.ToItem())
                .Where(entry => !eventItems.Any(evt => TimelineItem.IsSameLog(entry, evt)))
                .Concat(eventItems)
                .OrderBy(i => i.Timestamp)
                .ThenBy(i => i.Sequence)
                .ToList();

            return Results.Ok(new SessionTimelineResult(session.ToSummary(), items, truncated));
        }).RequireAuthorization();
    }

    /// <summary>A session visible to the caller: a project-scoped key only sees its own project (404 otherwise).</summary>
    private static async Task<AppSession?> FindAsync(IntelligenceDbContext db, ClaimsPrincipal user, Guid id)
    {
        var session = await db.Sessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        var scope = user.ProjectScope();
        return session is null || (scope is not null && session.ProjectId != scope) ? null : session;
    }
}
