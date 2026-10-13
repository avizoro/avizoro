using Avizoro.Core.Models;

namespace Avizoro.Core.Services;

/// <summary>
/// Tracks app sessions for release health (crash-free sessions / users). Platform
/// SDKs drive it from app lifecycle events: start at launch, pause when the app
/// goes to the background, resume when it returns. A resume after
/// <c>AvizoroOptions.SessionTimeout</c> in the background starts a new session.
/// </summary>
public interface ISessionTracker
{
    /// <summary>The live session, or null before the first start / after it ended.</summary>
    SessionInfo? Current { get; }

    Task StartAsync();

    Task PauseAsync();

    Task ResumeAsync();

    Task EndAsync();

    /// <summary>Counts a handled error against the current session (sent with its next update).</summary>
    void RecordError();

    /// <summary>Associates later session updates with a user id (null clears it).</summary>
    void SetUser(string? userId);

    /// <summary>
    /// Marks the current session crashed and persists that WITHOUT a network call
    /// (called from crash handlers; it is uploaded on the next launch).
    /// </summary>
    Task CaptureCrashAsync();

    /// <summary>
    /// Closes a session from an earlier run as crashed, from the last snapshot the
    /// SDK kept of it. Persist-only. Default implementation does nothing.
    /// </summary>
    Task CapturePreviousRunCrashAsync(SessionInfo session) => Task.CompletedTask;
}
