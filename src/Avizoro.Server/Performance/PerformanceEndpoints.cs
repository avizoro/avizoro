using System.Security.Claims;
using Avizoro.Core.Models;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Performance;

/// <summary>Stores SDK performance batches and serves percentile summaries.</summary>
public static class PerformanceEndpoints
{
    /// <summary>Upper bound on spans aggregated per request (newest first).</summary>
    private const int MaxRows = 100_000;

    private const int MaxNameLength = 300;

    public static async Task IngestAsync(AvizoroDbContext db, AvizoroEvent e, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var span in e.Spans!.Take(500))
        {
            if (string.IsNullOrWhiteSpace(span.Operation) || span.DurationMs < 0 || double.IsNaN(span.DurationMs))
                continue;

            db.Spans.Add(new StoredSpan
            {
                Id = Guid.NewGuid(),
                ProjectId = e.ProjectId,
                Release = e.Release,
                Environment = e.Environment,
                Platform = e.Platform,
                Operation = Truncate(span.Operation, 64),
                Name = Truncate(span.Name, MaxNameLength),
                Start = span.Start == default ? now : span.Start,
                DurationMs = span.DurationMs,
                StatusCode = span.StatusCode,
                Success = span.Success,
                ReceivedAt = now
            });
        }

        await db.SaveChangesAsync(ct);
    }

    public static void MapPerformanceEndpoints(this WebApplication app)
    {
        // Per (operation, name) latency percentiles over the last N days, slowest p95
        // first. Aggregated in memory over a bounded, newest-first sample.
        app.MapGet("/performance", async (AvizoroDbContext db, ClaimsPrincipal user,
            string? projectId, string? operation, string? release, string? environment, int days = 7) =>
        {
            days = Math.Clamp(days, 1, 90);
            var since = DateTime.UtcNow.AddDays(-days);

            var query = db.Spans.AsNoTracking().Where(s => s.Start >= since);
            var projectFilter = user.ProjectScope() ?? projectId;
            if (!string.IsNullOrWhiteSpace(projectFilter))
                query = query.Where(s => s.ProjectId == projectFilter);
            if (!string.IsNullOrWhiteSpace(operation))
                query = query.Where(s => s.Operation == operation);
            if (!string.IsNullOrWhiteSpace(release))
                query = query.Where(s => s.Release == release);
            if (!string.IsNullOrWhiteSpace(environment))
                query = query.Where(s => s.Environment == environment);

            var rows = await query
                .OrderByDescending(s => s.Start)
                .Take(MaxRows)
                .Select(s => new { s.Operation, s.Name, s.DurationMs, s.Success })
                .ToListAsync();

            var summaries = rows
                .GroupBy(r => (r.Operation, r.Name))
                .Select(g =>
                {
                    var durations = g.Select(r => r.DurationMs).OrderBy(d => d).ToArray();
                    return new PerformanceSummary(
                        g.Key.Operation, g.Key.Name, durations.Length,
                        Math.Round(durations.Average(), 1),
                        Percentile(durations, 0.50), Percentile(durations, 0.75), Percentile(durations, 0.95),
                        Math.Round(g.Count(r => !r.Success) / (double)durations.Length, 4));
                })
                .OrderByDescending(s => s.P95Ms)
                .ToList();

            return Results.Ok(summaries);
        }).RequireAuthorization();
    }

    /// <summary>Nearest-rank percentile of an ascending array.</summary>
    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
            return 0;
        var rank = (int)Math.Ceiling(p * sorted.Length) - 1;
        return Math.Round(sorted[Math.Clamp(rank, 0, sorted.Length - 1)], 1);
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
