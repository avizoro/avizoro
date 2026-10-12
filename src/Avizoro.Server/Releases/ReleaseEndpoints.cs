using System.Security.Claims;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Avizoro.Server.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Releases;

/// <summary>
/// Release health: per-release adoption, crash-free rates, event volume and new
/// issues, so a bad release is obvious at a glance. Combines sessions, events and
/// issues; everything is grouped in memory over a bounded window (provider-neutral).
/// </summary>
public static class ReleaseEndpoints
{
    public static void MapReleaseEndpoints(this WebApplication app)
    {
        app.MapGet("/releases", async (AvizoroDbContext db, ClaimsPrincipal user,
            string? projectId, string? environment, int days = 30) =>
        {
            days = Math.Clamp(days, 1, 90);
            var now = DateTime.UtcNow;
            var windowStart = now.Date.AddDays(-(days - 1));
            var adoptionStart = now.AddHours(-24);
            var projectFilter = user.ProjectScope() ?? projectId;

            var sessions = await SessionEndpoints.Query(db, user, projectId, environment, release: null)
                .Where(s => s.Started >= windowStart)
                .Select(s => new SessionEndpoints.SessionRow(s.Status, s.Errors, s.DistinctId, s.UserId, s.Started, s.LastUpdate, s.Release))
                .ToListAsync();

            var eventQuery = db.Events.AsNoTracking().Where(e => e.ReceivedAt >= windowStart && e.Release != "");
            if (!string.IsNullOrWhiteSpace(projectFilter))
                eventQuery = eventQuery.Where(e => e.ProjectId == projectFilter);
            if (!string.IsNullOrWhiteSpace(environment))
                eventQuery = eventQuery.Where(e => e.Environment == environment);
            var events = await eventQuery.Select(e => new { e.Release, e.EventType, e.ReceivedAt }).ToListAsync();

            var issueQuery = db.Issues.AsNoTracking().Where(i => i.FirstRelease != null);
            if (!string.IsNullOrWhiteSpace(projectFilter))
                issueQuery = issueQuery.Where(i => i.ProjectId == projectFilter);
            var newIssues = (await issueQuery.Select(i => i.FirstRelease!).ToListAsync())
                .GroupBy(r => r)
                .ToDictionary(g => g.Key, g => g.Count());

            var recentSessions = sessions.Count(s => s.Started >= adoptionStart);

            var releases = sessions.Select(s => s.Release)
                .Concat(events.Select(e => e.Release))
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct()
                .Select(release =>
                {
                    var rs = sessions.Where(s => s.Release == release).ToList();
                    var re = events.Where(e => e.Release == release).ToList();
                    var stats = SessionEndpoints.Summarize(rs, []);

                    var seen = rs.Select(s => s.Started).Concat(re.Select(e => e.ReceivedAt)).ToList();
                    var adoption = recentSessions == 0
                        ? (double?)null
                        : Math.Round(rs.Count(s => s.Started >= adoptionStart) / (double)recentSessions, 4);

                    return new ReleaseHealth(
                        release, seen.Min(), seen.Max(),
                        stats.Sessions, stats.CrashedSessions, stats.CrashFreeSessionRate,
                        stats.Users, stats.CrashFreeUserRate, adoption,
                        re.Count, re.Count(e => e.EventType == "Exception"),
                        newIssues.GetValueOrDefault(release));
                })
                .OrderByDescending(r => r.LastSeen)
                .ToList();

            return Results.Ok(releases);
        }).RequireAuthorization();
    }
}
