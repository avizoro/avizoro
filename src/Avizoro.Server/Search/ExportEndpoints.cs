using System.Globalization;
using System.Security.Claims;
using System.Text;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Search;

/// <summary>
/// CSV / JSON exports of events (same filters as <c>GET /events</c>) and issues,
/// capped at <see cref="MaxRows"/> newest rows.
/// </summary>
public static class ExportEndpoints
{
    public const int MaxRows = 10_000;

    public static void MapExportEndpoints(this WebApplication app)
    {
        app.MapGet("/events/export", async (AvizoroDbContext db, ClaimsPrincipal user, [AsParameters] EventFilter filter, string? format) =>
        {
            var rows = await EventSearch.Apply(db.Events.AsNoTracking(), filter, user.ProjectScope())
                .OrderByDescending(e => e.ReceivedAt)
                .Take(MaxRows)
                .Select(e => new
                {
                    e.Id, e.ReceivedAt, e.Timestamp, e.ProjectId, e.EventType, e.Level, e.Release, e.Environment,
                    e.Platform, e.OperatingSystem, e.DeviceModel, e.UserId, e.ExceptionType, e.ExceptionMessage, e.Message
                })
                .ToListAsync();

            if (IsJson(format))
                return Results.Json(rows, contentType: "application/json");

            var csv = Csv(
                ["id", "receivedAt", "timestamp", "projectId", "eventType", "level", "release", "environment", "platform",
                 "operatingSystem", "deviceModel", "userId", "exceptionType", "exceptionMessage", "message"],
                rows.Select(r => new object?[]
                {
                    r.Id, r.ReceivedAt, r.Timestamp, r.ProjectId, r.EventType, r.Level, r.Release, r.Environment, r.Platform,
                    r.OperatingSystem, r.DeviceModel, r.UserId, r.ExceptionType, r.ExceptionMessage, r.Message
                }));
            return Results.File(csv, "text/csv; charset=utf-8", $"events-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }).RequireAuthorization();

        app.MapGet("/issues/export", async (AvizoroDbContext db, ClaimsPrincipal user, string? projectId, string? status, string? format) =>
        {
            var query = db.Issues.AsNoTracking().AsQueryable();
            var projectFilter = user.ProjectScope() ?? projectId;
            if (!string.IsNullOrWhiteSpace(projectFilter))
                query = query.Where(i => i.ProjectId == projectFilter);
            if (IssueStatuses.Normalize(status) is { } s)
                query = query.Where(i => i.Status == s);

            var issues = await query.OrderByDescending(i => i.LastSeen).Take(MaxRows).ToListAsync();

            if (IsJson(format))
                return Results.Json(issues.Select(i => i.ToSummary()), contentType: "application/json");

            var csv = Csv(
                ["id", "projectId", "title", "culprit", "eventType", "level", "status", "isRegression", "assignedTo",
                 "eventCount", "firstSeen", "lastSeen", "firstRelease", "lastRelease", "externalIssueUrl"],
                issues.Select(i => new object?[]
                {
                    i.Id, i.ProjectId, i.Title, i.Culprit, i.EventType, i.Level, i.Status, i.IsRegression, i.AssignedTo,
                    i.EventCount, i.FirstSeen, i.LastSeen, i.FirstRelease, i.LastRelease, i.ExternalIssueUrl
                }));
            return Results.File(csv, "text/csv; charset=utf-8", $"issues-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
        }).RequireAuthorization();
    }

    private static bool IsJson(string? format) => string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    /// <summary>RFC 4180 CSV with a UTF-8 BOM (so Excel detects the encoding).</summary>
    public static byte[] Csv(string[] header, IEnumerable<object?[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', header.Select(Escape)).Append("\r\n");
        foreach (var row in rows)
            sb.AppendJoin(',', row.Select(v => Escape(Format(v)))).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static string Escape(string value)
    {
        // Neutralize spreadsheet formula injection (=, +, -, @ at the start).
        if (value.Length > 0 && "=+-@".Contains(value[0]))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
