namespace Avizoro.Core.Enums;

/// <summary>State of an app session, as reported for release health.</summary>
public enum SessionStatus
{
    /// <summary>In progress (or backgrounded but still resumable).</summary>
    Ok,

    /// <summary>Ended normally.</summary>
    Exited,

    /// <summary>Ended by a fatal, unhandled crash.</summary>
    Crashed,

    /// <summary>Ended without the SDK seeing it end (e.g. killed by the OS). Assigned server-side.</summary>
    Abnormal
}
