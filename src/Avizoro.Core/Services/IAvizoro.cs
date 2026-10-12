using Avizoro.Core.Enums;
using Avizoro.Core.Models;

namespace Avizoro.Core.Services;

public interface IAvizoro
{
    Task TrackAsync(AvizoroEvent avizoroEvent);

    Task TrackExceptionAsync(Exception exception);

    Task TrackExceptionAsync(ExceptionInfo exception);

    /// <summary>
    /// Captures a log message as an event (Sentry's "capture message"). Also
    /// records it as a breadcrumb so it shows up in the trail of later events.
    /// </summary>
    Task TrackLogAsync(SeverityLevel level, string message, IDictionary<string, string>? data = null);

    /// <summary>
    /// Adds a breadcrumb to the local trail. Breadcrumbs are NOT sent on their
    /// own — they ride along with the next event that is captured.
    /// </summary>
    void AddBreadcrumb(string message, string category = BreadcrumbCategories.Custom,
        SeverityLevel level = SeverityLevel.Information, IDictionary<string, string>? data = null);

    /// <summary>Associates subsequent events with a user id (opt-in). Pass null to clear.</summary>
    void SetUser(string? userId);

    /// <summary>Sets an indexable tag on subsequent events. Pass null value to remove.</summary>
    void SetTag(string key, string? value);

    /// <summary>
    /// Persists a fatal exception to the offline store WITHOUT attempting a
    /// network send. Used from crash handlers, where the process is about to
    /// die: a local write is fast and reliable, and the event is uploaded on the
    /// next launch. Avoids blocking the crashing thread on a network call.
    /// </summary>
    Task CaptureCrashAsync(ExceptionInfo exception);

    /// <summary>
    /// Id of the most recent exception/crash/log event captured in this process,
    /// to attach <see cref="CaptureFeedbackAsync"/> to. Null when none yet.
    /// </summary>
    Guid? LastEventId => null;

    /// <summary>
    /// Sends what the user says happened, linked to <see cref="UserFeedback.EventId"/>.
    /// Default implementation does nothing (keeps third-party implementations compiling).
    /// </summary>
    Task CaptureFeedbackAsync(UserFeedback feedback) => Task.CompletedTask;
}
