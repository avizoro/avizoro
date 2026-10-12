using Avizoro.Core.Storage;

namespace Avizoro.Core.Services;

// EventUploader drains the offline queue and, for delivered events, ships any
// attached screenshot blob.

public class EventUploader : IEventUploader
{
    private const int BatchSize = 50;

    private readonly IEventStore _store;
    private readonly IAvizoroClient _client;
    private readonly IScreenshotStore _screenshots;

    // Ensures only one drain runs at a time; extra callers return immediately.
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EventUploader(IEventStore store, IAvizoroClient client, IScreenshotStore screenshots)
    {
        _store = store;
        _client = client;
        _screenshots = screenshots;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var batch = await _store.GetPendingAsync(BatchSize).ConfigureAwait(false);
                if (batch.Count == 0)
                    return;

                foreach (var avizoroEvent in batch)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    var result = await _client.SendAsync(avizoroEvent, cancellationToken).ConfigureAwait(false);

                    if (result == SendResult.TransientFailure)
                        return; // keep this event (and the rest); retry on the next flush

                    // The event is on the server (delivered). Ship its screenshot, if
                    // any, before dropping the event. Re-sending a delivered event is
                    // safe because the server ingest is idempotent by event id.
                    if (result == SendResult.Delivered)
                    {
                        var jpeg = await _screenshots.GetAsync(avizoroEvent.Id).ConfigureAwait(false);
                        if (jpeg is { Length: > 0 })
                        {
                            var shotResult = await _client
                                .SendScreenshotAsync(avizoroEvent.Id, jpeg, cancellationToken)
                                .ConfigureAwait(false);

                            if (shotResult == SendResult.TransientFailure)
                                return; // keep event + screenshot; retry next flush

                            await _screenshots.DeleteAsync(avizoroEvent.Id).ConfigureAwait(false);
                        }
                    }

                    // Delivered (with screenshot handled) or permanently Rejected.
                    await _store.DeleteAsync(avizoroEvent.Id).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
