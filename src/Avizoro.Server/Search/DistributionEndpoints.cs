using System.Security.Claims;
using System.Text.Json;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Search;

/// <summary>
/// "Who is this happening to?" — for one issue, the top values of the device /
/// release context and of every tag across its most recent events.
/// </summary>
public static class DistributionEndpoints
{
    private const int SampleSize = 5000;
    private const int TopValues = 5;
    private const int MaxTagKeys = 10;

    public static void MapDistributionEndpoints(this WebApplication app)
    {
        app.MapGet("/issues/{id:guid}/distributions", async (Guid id, AvizoroDbContext db, ClaimsPrincipal user) =>
        {
            var issue = await db.Issues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            var scope = user.ProjectScope();
            if (issue is null || (scope is not null && issue.ProjectId != scope))
                return Results.NotFound();

            var rows = await db.Events.AsNoTracking()
                .Where(e => e.ProjectId == issue.ProjectId && e.Fingerprint == issue.Fingerprint)
                .OrderByDescending(e => e.ReceivedAt)
                .Take(SampleSize)
                .Select(e => new
                {
                    e.Platform, e.OperatingSystem, e.DeviceModel, e.Manufacturer,
                    e.Release, e.Environment, e.ApplicationVersion, e.UserId, e.TagsJson
                })
                .ToListAsync();

            var result = new List<Distribution>
            {
                Distribute("platform", rows.Select(r => r.Platform)),
                Distribute("os", rows.Select(r => r.OperatingSystem)),
                Distribute("device", rows.Select(r => r.DeviceModel)),
                Distribute("manufacturer", rows.Select(r => r.Manufacturer)),
                Distribute("release", rows.Select(r => r.Release)),
                Distribute("environment", rows.Select(r => r.Environment)),
            };

            // Tags: the most common keys, each with its top values.
            var tags = rows
                .Select(r => r.TagsJson is null ? null : Parse(r.TagsJson))
                .ToList();
            var keys = tags.Where(t => t is not null)
                .SelectMany(t => t!.Keys)
                .GroupBy(k => k)
                .OrderByDescending(g => g.Count())
                .Take(MaxTagKeys)
                .Select(g => g.Key);
            foreach (var key in keys)
                result.Add(Distribute($"tag:{key}", tags.Select(t => t is not null && t.TryGetValue(key, out var v) ? v : null)));

            var affectedUsers = rows.Select(r => r.UserId).Where(u => !string.IsNullOrEmpty(u)).Distinct().Count();

            return Results.Ok(new IssueDistributions(rows.Count, affectedUsers, result.Where(d => d.Values.Count > 0).ToList()));
        }).RequireAuthorization();
    }

    /// <summary>Top values with their share; the rest collapse into "(other)". Blank values are skipped.</summary>
    public static Distribution Distribute(string key, IEnumerable<string?> values)
    {
        var present = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();
        var total = present.Count;
        var groups = present.GroupBy(v => v).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).ToList();

        var top = groups.Take(TopValues)
            .Select(g => new DistributionValue(g.Key, g.Count(), Math.Round(g.Count() / (double)total, 4)))
            .ToList();
        var other = groups.Skip(TopValues).Sum(g => g.Count());
        if (other > 0)
            top.Add(new DistributionValue("(other)", other, Math.Round(other / (double)total, 4)));

        return new Distribution(key, total, top);
    }

    private static Dictionary<string, string>? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
