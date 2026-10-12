using System.Threading.Channels;
using Avizoro.Server.Contracts;

namespace Avizoro.Server.Alerts;

/// <summary>
/// Everything a notifier needs to deliver one alert, snapshotted at enqueue time
/// so the dispatcher never has to go back to the database to format it.
/// </summary>
public sealed record AlertJob(
    Guid NotificationId,
    string RuleName,
    string Trigger,
    string Channel,
    string Target,
    string? Secret,
    IssueSummary Issue,
    EventSummary? Event,
    int? WindowCount = null,
    int? WindowMinutes = null);

/// <summary>
/// In-process hand-off between ingest (producer) and <see cref="AlertDispatcher"/>
/// (consumer), so a slow or dead webhook never delays <c>POST /events</c>.
/// Notifications are persisted as pending before being queued, so anything lost
/// in a restart is re-queued by the dispatcher on startup.
/// </summary>
public sealed class AlertQueue
{
    private readonly Channel<AlertJob> _channel = Channel.CreateUnbounded<AlertJob>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(AlertJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<AlertJob> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
