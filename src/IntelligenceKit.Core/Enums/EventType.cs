namespace IntelligenceKit.Core.Enums;

public enum EventType
{
    Exception,
    Log,
    Performance,
    Navigation,
    UserAction,
    DeviceInfo,
    Unknown,

    /// <summary>
    /// A session start/update/end (see <see cref="Models.SessionInfo"/>). Rides the
    /// same store-and-forward queue as other events, but the server records it
    /// as release-health data instead of an issue. Appended last so the numeric
    /// values of the existing members stay stable on the wire.
    /// </summary>
    Session,

    /// <summary>User-written feedback about an earlier event (see <see cref="Models.UserFeedback"/>).</summary>
    Feedback,

    /// <summary>
    /// A batch of session timeline entries (<see cref="Models.IntelligenceEvent.Breadcrumbs"/>
    /// recorded during one session). Sent only with <c>EnableSessionTimeline</c>; the server
    /// stores them per session instead of creating an issue.
    /// </summary>
    Timeline
}