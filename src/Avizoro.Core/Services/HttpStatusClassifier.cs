namespace Avizoro.Core.Services;

/// <summary>
/// Maps an HTTP status code from an ingest attempt to a <see cref="SendResult"/>,
/// deciding whether the offline queue should drop the event or keep and retry it.
///
/// Lives in Core (not the MAUI client) so the store-and-forward contract is
/// unit-testable without the platform SDK.
/// </summary>
public static class HttpStatusClassifier
{
    public static SendResult Classify(int statusCode)
    {
        if (statusCode is >= 200 and < 300)
            return SendResult.Delivered;

        // 429 (rate limited) and 408 (request timeout) are the server asking us to
        // back off, not a permanent rejection — keep the event and retry later.
        // This is what makes ingest rate limiting safe: throttled events are
        // deferred by store-and-forward, never dropped.
        if (statusCode is 429 or 408)
            return SendResult.TransientFailure;

        // Any other 4xx means the server rejected this event for good; retrying
        // won't change that, so drop it rather than poison the queue.
        if (statusCode is >= 400 and < 500)
            return SendResult.Rejected;

        // 5xx and anything else: transient, worth retrying.
        return SendResult.TransientFailure;
    }
}
