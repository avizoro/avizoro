namespace Avizoro.Core.Diagnostics;

/// <summary>
/// Platform bridge for <see cref="UiThreadWatchdog"/>: queues work on the UI
/// (main) thread and, where the platform allows it, snapshots that thread's stack.
/// </summary>
public interface IUiThreadDispatcher
{
    /// <summary>Queues <paramref name="action"/> to run on the UI thread (never inline).</summary>
    void Post(Action action);

    /// <summary>
    /// The UI thread's current stack, captured while it is blocked, or null when the
    /// platform can't provide it.
    /// </summary>
    string? CaptureUiThreadStack();
}
