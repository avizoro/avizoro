using Avizoro.Server.Contracts;
using Avizoro.Server.Data;

namespace Avizoro.Server;

/// <summary>Entity → contract projections shared by the endpoints and the live push.</summary>
public static class Mapping
{
    public static IssueSummary ToSummary(this Issue i, int recentCount = 0, int previousCount = 0)
        => new(
            i.Id, i.ProjectId, i.Fingerprint, i.Title, i.Culprit, i.EventType, i.Level,
            i.EventCount, i.FirstSeen, i.LastSeen, i.LastEventId, recentCount, previousCount,
            i.Status, i.IsRegression, i.AssignedTo, i.ResolvedAt, i.ResolvedInRelease, i.RegressedAt,
            i.FirstRelease, i.LastRelease, i.ExternalIssueUrl);

    public static EventSummary ToSummary(this StoredEvent e)
        => new(
            e.Id, e.ProjectId, e.ApplicationName, e.ApplicationVersion,
            e.Environment, e.Platform, e.DeviceName, e.EventType,
            e.Level, e.UserId,
            e.ExceptionType, e.ExceptionMessage, e.Message,
            e.Timestamp, e.ReceivedAt, e.SessionId);

    public static SessionSummary ToSummary(this AppSession s)
        => new(
            s.Id, s.ProjectId, s.Status,
            s.Status == "Ok" && DateTime.UtcNow - s.LastUpdate > Sessions.SessionEndpoints.AbnormalAfter,
            s.Started, s.LastUpdate, s.LastActivity, s.DurationSeconds, s.Errors, s.TimelineEntries,
            s.Release, s.Environment, s.Platform, s.DeviceModel, s.Manufacturer, s.OperatingSystem,
            s.UserId, s.DistinctId);

    public static TimelineItem ToItem(this TimelineEntry e)
        => new(
            TimelineItem.EntryKind, e.Id, e.Timestamp, e.Sequence, e.Category, e.Message, e.Level,
            e.DataJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(e.DataJson),
            ScreenshotId: e.Category == Avizoro.Core.Models.BreadcrumbCategories.Screenshot ? e.BatchId : null);
}

public static class TimelineMapping
{
    /// <summary>An error/log event on a session timeline. Sorts after steps with the same timestamp.</summary>
    public static TimelineItem EventItem(Guid id, DateTime timestamp, string eventType, string? level, string? message, string? exceptionType, string? exceptionMessage)
        => new(
            TimelineItem.EventKind, id, timestamp, long.MaxValue,
            eventType == "Log" ? "log" : "error",
            exceptionType ?? message ?? eventType,
            level ?? "Error",
            Data: null,
            EventId: id,
            EventType: eventType,
            ExceptionType: exceptionType,
            ExceptionMessage: exceptionMessage,
            EventMessage: message);

    public static TimelineItem EventItem(this StoredEvent e)
        => EventItem(e.Id, e.Timestamp, e.EventType, e.Level, e.Message, e.ExceptionType, e.ExceptionMessage);
}
