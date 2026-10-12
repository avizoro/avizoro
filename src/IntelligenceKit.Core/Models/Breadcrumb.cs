using IntelligenceKit.Core.Enums;

namespace IntelligenceKit.Core.Models;

/// <summary>
/// A single entry in the trail of activity leading up to an event. Breadcrumbs
/// live in a local ring buffer on the device and are attached to events when
/// they are captured — so a crash arrives with the story of what happened
/// before it, not just the stack trace.
/// </summary>
public class Breadcrumb
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>Free-form grouping, e.g. "navigation", "log", "user", "http".</summary>
    public string Category { get; set; } = "custom";

    public string Message { get; set; } = string.Empty;

    public SeverityLevel Level { get; set; } = SeverityLevel.Information;

    /// <summary>Optional structured detail (route names, ids, status codes…).</summary>
    public Dictionary<string, string> Data { get; set; } = new();
}

/// <summary>Well-known breadcrumb categories, for consistency across the SDK.</summary>
public static class BreadcrumbCategories
{
    public const string Navigation = "navigation";
    public const string Log = "log";
    public const string User = "user";
    public const string Http = "http";
    public const string System = "system";
    public const string Custom = "custom";

    /// <summary>A screenshot taken on demand; only appears on session timelines.</summary>
    public const string Screenshot = "screenshot";
}
