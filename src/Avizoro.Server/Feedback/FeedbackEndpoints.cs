using System.Security.Claims;
using Avizoro.Core.Models;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Avizoro.Server.Feedback;

/// <summary>Stores user feedback and lists it per issue / event / project.</summary>
public static class FeedbackEndpoints
{
    private const int MaxCommentLength = 4000;

    public static async Task IngestAsync(AvizoroDbContext db, AvizoroEvent e, CancellationToken ct = default)
    {
        var f = e.Feedback!;

        // Link to the issue now when the event is already here (the SDK queue sends
        // the crash before its feedback); otherwise it's resolved on read.
        var issueId = await IssueOfEventAsync(db, f.EventId, ct);

        db.Feedback.Add(new Data.Feedback
        {
            Id = Guid.NewGuid(),
            ProjectId = e.ProjectId,
            EventId = f.EventId,
            IssueId = issueId,
            Comments = Truncate(f.Comments.Trim(), MaxCommentLength)!,
            Name = Truncate(f.Name?.Trim(), 200),
            Email = Truncate(f.Email?.Trim(), 320),
            UserId = e.UserId,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public static void MapFeedbackEndpoints(this WebApplication app)
    {
        app.MapGet("/issues/{id:guid}/feedback", async (Guid id, AvizoroDbContext db, ClaimsPrincipal user) =>
        {
            var issue = await db.Issues.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            var scope = user.ProjectScope();
            if (issue is null || (scope is not null && issue.ProjectId != scope))
                return Results.NotFound();

            // Feedback linked at ingest, plus feedback that arrived before its event.
            var eventIds = db.Events.Where(e => e.ProjectId == issue.ProjectId && e.Fingerprint == issue.Fingerprint).Select(e => e.Id);
            var items = await db.Feedback.AsNoTracking()
                .Where(f => f.IssueId == id || (f.IssueId == null && eventIds.Contains(f.EventId)))
                .OrderByDescending(f => f.CreatedAt)
                .Take(200)
                .Select(f => new FeedbackInfo(f.Id, f.ProjectId, f.EventId, f.IssueId, f.Comments, f.Name, f.Email, f.UserId, f.CreatedAt))
                .ToListAsync();
            return Results.Ok(items);
        }).RequireAuthorization();

        app.MapGet("/events/{id:guid}/feedback", async (Guid id, AvizoroDbContext db, ClaimsPrincipal user) =>
        {
            var scope = user.ProjectScope();
            var items = await db.Feedback.AsNoTracking()
                .Where(f => f.EventId == id && (scope == null || f.ProjectId == scope))
                .OrderByDescending(f => f.CreatedAt)
                .Select(f => new FeedbackInfo(f.Id, f.ProjectId, f.EventId, f.IssueId, f.Comments, f.Name, f.Email, f.UserId, f.CreatedAt))
                .ToListAsync();
            return Results.Ok(items);
        }).RequireAuthorization();

        app.MapGet("/feedback", async (AvizoroDbContext db, ClaimsPrincipal user, string? projectId, int skip = 0, int take = 50) =>
        {
            take = Math.Clamp(take, 1, 200);
            var query = db.Feedback.AsNoTracking().AsQueryable();
            var projectFilter = user.ProjectScope() ?? projectId;
            if (!string.IsNullOrWhiteSpace(projectFilter))
                query = query.Where(f => f.ProjectId == projectFilter);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(f => f.CreatedAt)
                .Skip(skip)
                .Take(take)
                .Select(f => new FeedbackInfo(f.Id, f.ProjectId, f.EventId, f.IssueId, f.Comments, f.Name, f.Email, f.UserId, f.CreatedAt))
                .ToListAsync();
            return Results.Ok(new PagedResult<FeedbackInfo>(total, skip, take, items));
        }).RequireAuthorization();
    }

    private static async Task<Guid?> IssueOfEventAsync(AvizoroDbContext db, Guid eventId, CancellationToken ct)
    {
        var e = await db.Events.AsNoTracking()
            .Where(x => x.Id == eventId)
            .Select(x => new { x.ProjectId, x.Fingerprint })
            .FirstOrDefaultAsync(ct);
        if (e is null)
            return null;

        return await db.Issues.AsNoTracking()
            .Where(i => i.ProjectId == e.ProjectId && i.Fingerprint == e.Fingerprint)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);
    }

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
