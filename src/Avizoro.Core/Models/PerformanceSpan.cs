namespace Avizoro.Core.Models;

/// <summary>
/// One timed operation: an app start, a page load or an HTTP request. Spans are
/// batched on the device and shipped together in a single
/// <see cref="Enums.EventType.Performance"/> event.
/// </summary>
public class PerformanceSpan
{
    /// <summary>Kind of operation — see <see cref="SpanOperations"/>.</summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>What was measured, e.g. a page name or "GET api.example.com/orders/{id}".</summary>
    public string Name { get; set; } = string.Empty;

    public DateTime Start { get; set; } = DateTime.UtcNow;

    public double DurationMs { get; set; }

    /// <summary>HTTP status code, for <see cref="SpanOperations.Http"/> spans.</summary>
    public int? StatusCode { get; set; }

    /// <summary>False when the operation failed (exception or HTTP 5xx).</summary>
    public bool Success { get; set; } = true;
}

/// <summary>Well-known span operations.</summary>
public static class SpanOperations
{
    /// <summary>Process start (or SDK init) to the first screen.</summary>
    public const string AppStart = "app.start";

    /// <summary>Navigation away from a page until the next page appears.</summary>
    public const string PageLoad = "ui.load";

    /// <summary>An outgoing HTTP request.</summary>
    public const string Http = "http.client";
}
