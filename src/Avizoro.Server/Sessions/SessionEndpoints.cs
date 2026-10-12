using System.Security.Claims;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Sessions;

/// <summary>Release-health reads built on the <see cref="AppSession"/> table.</summary>
public static class SessionEndpoints
{
    /// <summary>An "Ok" session silent for this long is counted as abnormal.</summary>
    public static readonly TimeSpan AbnormalAfter = TimeSpan.FromHours(24);

    public static void MapSessionEndpoints(this WebApplication app)
    {
        // Crash-free sessions/users over the last N days, plus a daily series.
        // Aggregated in memory (bounded window): the GroupBy shape and date functions differ across SQLite, PostgreSQL and SQL Server.
        app.MapGet("/stats/crash-free", async (AvizoroDbContext db, ClaimsPrincipal user,
            string? projectId, string? environment, string? release, int days = 14) =>
        {
            days = Math.Clamp(days, 1, 90);
            var today = DateTime.UtcNow.Date;
            var windowStart = today.AddDays(-(days - 1));

            var rows = await Query(db, user, projectId, environment, release)
                .Where(s => s.Started >= windowStart)
                .Select(s => new SessionRow(s.Status, s.Errors, s.DistinctId, s.UserId, s.Started, s.LastUpdate, s.Release))
                .ToListAsync();

            var daily = Enumerable.Range(0, days).Select(i =>
            {
                var day = windowStart.AddDays(i);
                var inDay = rows.Where(r => r.Started.Date == day).ToList();
                var crashed = inDay.Count(r => r.IsCrashed);
                return new CrashFreeDay(day, inDay.Count, crashed, Rate(inDay.Count, crashed));
            }).ToList();

            return Results.Ok(Summarize(rows, daily));
        }).RequireAuthorization();
    }

    /// <summary>Sessions visible to the caller, filtered by project/environment/release.</summary>
    public static IQueryable<AppSession> Query(AvizoroDbContext db, ClaimsPrincipal user,
        string? projectId, string? environment, string? release)
    {
        var query = db.Sessions.AsNoTracking().AsQueryable();
        var projectFilter = user.ProjectScope() ?? projectId;
        if (!string.IsNullOrWhiteSpace(projectFilter))
            query = query.Where(s => s.ProjectId == projectFilter);
        if (!string.IsNullOrWhiteSpace(environment))
            query = query.Where(s => s.Environment == environment);
        if (!string.IsNullOrWhiteSpace(release))
            query = query.Where(s => s.Release == release);
        return query;
    }

    public static CrashFreeStats Summarize(IReadOnlyCollection<SessionRow> rows, IReadOnlyList<CrashFreeDay> daily)
    {
        var crashed = rows.Count(r => r.IsCrashed);
        var errored = rows.Count(r => !r.IsCrashed && r.Errors > 0);
        var abnormal = rows.Count(r => r.IsAbnormal);

        var users = rows.GroupBy(r => r.UserKey).Where(g => g.Key.Length > 0).ToList();
        var crashedUsers = users.Count(g => g.Any(r => r.IsCrashed));

        return new CrashFreeStats(
            rows.Count, crashed, errored, abnormal, Rate(rows.Count, crashed),
            users.Count, crashedUsers, Rate(users.Count, crashedUsers),
            daily);
    }

    public static double? Rate(int total, int crashed)
        => total == 0 ? null : Math.Round(1.0 - (double)crashed / total, 5);

    public sealed record SessionRow(
        string Status, int Errors, string DistinctId, string? UserId, DateTime Started, DateTime LastUpdate, string Release)
    {
        public bool IsCrashed => Status == "Crashed";

        public bool IsAbnormal => Status == "Ok" && DateTime.UtcNow - LastUpdate > AbnormalAfter;

        /// <summary>A known user id wins over the anonymous installation id.</summary>
        public string UserKey => string.IsNullOrWhiteSpace(UserId) ? DistinctId : "u:" + UserId;
    }
}
