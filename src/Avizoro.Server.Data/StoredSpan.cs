namespace Avizoro.Server.Data;

/// <summary>A performance span (app start, page load, HTTP call) received from the SDK.</summary>
public class StoredSpan
{
    public Guid Id { get; set; }

    public string ProjectId { get; set; } = string.Empty;

    public string Release { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    /// <summary>app.start | ui.load | http.client (or a custom operation).</summary>
    public string Operation { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTime Start { get; set; }

    public double DurationMs { get; set; }

    public int? StatusCode { get; set; }

    public bool Success { get; set; } = true;

    public DateTime ReceivedAt { get; set; }
}
