using System.Text.Json;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;

namespace Avizoro.Server.Search;

/// <summary>
/// Translates an <see cref="EventFilter"/> into a provider-neutral EF query. Free
/// text and tags use <c>string.Contains</c> (LIKE/instr on every provider), so no
/// full-text index is needed; tags are matched as the exact <c>"key":"value"</c>
/// JSON fragment of the stored tag map.
/// </summary>
public static class EventSearch
{
    public static IQueryable<StoredEvent> Apply(IQueryable<StoredEvent> query, EventFilter f, string? projectScope)
    {
        // A scoped caller is pinned to its own project; the filter can't widen it.
        var project = projectScope ?? f.ProjectId;
        if (!string.IsNullOrWhiteSpace(project))
            query = query.Where(e => e.ProjectId == project);

        if (!string.IsNullOrWhiteSpace(f.EventType))
            query = query.Where(e => e.EventType == f.EventType);
        if (!string.IsNullOrWhiteSpace(f.Level))
            query = query.Where(e => e.Level == f.Level);
        if (!string.IsNullOrWhiteSpace(f.Release))
            query = query.Where(e => e.Release == f.Release);
        if (!string.IsNullOrWhiteSpace(f.Environment))
            query = query.Where(e => e.Environment == f.Environment);
        if (!string.IsNullOrWhiteSpace(f.Platform))
            query = query.Where(e => e.Platform == f.Platform);
        if (!string.IsNullOrWhiteSpace(f.UserId))
            query = query.Where(e => e.UserId == f.UserId);
        if (!string.IsNullOrWhiteSpace(f.OperatingSystem))
            query = query.Where(e => e.OperatingSystem.Contains(f.OperatingSystem));
        if (!string.IsNullOrWhiteSpace(f.DeviceModel))
            query = query.Where(e => e.DeviceModel.Contains(f.DeviceModel));
        if (f.From is { } from)
            query = query.Where(e => e.ReceivedAt >= from.ToUniversalTime());
        if (f.To is { } to)
            query = query.Where(e => e.ReceivedAt <= to.ToUniversalTime());

        if (!string.IsNullOrWhiteSpace(f.Q))
        {
            var q = f.Q.Trim();
            query = query.Where(e =>
                (e.Message != null && e.Message.Contains(q)) ||
                (e.ExceptionMessage != null && e.ExceptionMessage.Contains(q)) ||
                (e.ExceptionType != null && e.ExceptionType.Contains(q)));
        }

        foreach (var tag in f.Tag ?? [])
        {
            var separator = tag.IndexOf(':');
            if (separator <= 0)
                continue;

            // Same serializer as ingest, so escaping matches the stored JSON.
            var fragment = JsonSerializer.Serialize(tag[..separator]) + ":" + JsonSerializer.Serialize(tag[(separator + 1)..]);
            query = query.Where(e => e.TagsJson != null && e.TagsJson.Contains(fragment));
        }

        return query;
    }
}
