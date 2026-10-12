using System.Diagnostics.Metrics;

namespace IntelligenceKit.Server.Telemetry;

/// <summary>
/// The server's own metrics (meter <see cref="MeterName"/>), exported through
/// OpenTelemetry to Prometheus and/or OTLP when enabled (see Program.cs).
/// </summary>
public sealed class ServerMetrics
{
    public const string MeterName = "IntelligenceKit.Server";

    private readonly Counter<long> _eventsIngested;
    private readonly Counter<long> _eventsRejected;
    private readonly Counter<long> _issuesChanged;
    private readonly Counter<long> _sessionUpdates;
    private readonly Counter<long> _spansIngested;
    private readonly Counter<long> _timelineEntriesIngested;
    private readonly Counter<long> _alertsDelivered;
    private readonly Histogram<double> _ingestDuration;

    public ServerMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _eventsIngested = meter.CreateCounter<long>("ik.events.ingested", "{event}", "Events accepted by POST /events, by type.");
        _eventsRejected = meter.CreateCounter<long>("ik.events.rejected", "{event}", "Events refused at ingest, by reason.");
        _issuesChanged = meter.CreateCounter<long>("ik.issues.changed", "{issue}", "Issues created or regressed at ingest.");
        _sessionUpdates = meter.CreateCounter<long>("ik.sessions.updates", "{update}", "Session updates received, by status.");
        _spansIngested = meter.CreateCounter<long>("ik.spans.ingested", "{span}", "Performance spans stored.");
        _timelineEntriesIngested = meter.CreateCounter<long>("ik.timeline.entries.ingested", "{entry}", "Session timeline entries stored.");
        _alertsDelivered = meter.CreateCounter<long>("ik.alerts.delivered", "{alert}", "Alert deliveries, by channel and outcome.");
        _ingestDuration = meter.CreateHistogram<double>("ik.ingest.duration", "ms", "Time to process one POST /events.");
    }

    public void EventIngested(string eventType, string projectId)
        => _eventsIngested.Add(1, new("event_type", eventType), new("project", projectId));

    public void EventRejected(string reason) => _eventsRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void IssueChanged(string change) => _issuesChanged.Add(1, new KeyValuePair<string, object?>("change", change));

    public void SessionUpdate(string status) => _sessionUpdates.Add(1, new KeyValuePair<string, object?>("status", status));

    public void SpansIngested(int count) => _spansIngested.Add(count);

    public void TimelineEntriesIngested(int count) => _timelineEntriesIngested.Add(count);

    public void AlertDelivered(string channel, bool success)
        => _alertsDelivered.Add(1, new("channel", channel), new("outcome", success ? "sent" : "failed"));

    public void IngestDuration(TimeSpan elapsed) => _ingestDuration.Record(elapsed.TotalMilliseconds);
}
