#if IOS
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avizoro.Core.Models;
using Microsoft.Win32.SafeHandles;

namespace Avizoro.Maui.CrashReporting;

/// <summary>
/// Minimal record of a crash that managed code never sees: a fatal signal. The
/// handler only writes a 16-byte record (signal number + UNIX time) to a file that
/// is already open, then hands the signal back to whoever owned it before. The
/// next launch turns that record into a crash event (see CrashReporter.Apple.cs).
/// <para>
/// SIGSEGV, SIGBUS and SIGFPE are deliberately left alone: the .NET runtime uses
/// them for NullReferenceException and DivideByZeroException, and a handler on top
/// of the runtime's would record every one of those as a crash.
/// </para>
/// </summary>
internal static unsafe class NativeCrashMarker
{
    private const string MarkerFile = "avizoro.signal";
    private const string SessionFile = "avizoro.session";

    private const int SIGILL = 4, SIGTRAP = 5, SIGABRT = 6, SIGSYS = 12;
    private const int SignalCount = 4;

    // Darwin `struct sigaction`: handler pointer (8), sa_mask (4), sa_flags (4).
    private const int SigactionSize = 16;

    // Layout of the native block the handler works on (no managed objects, no
    // allocation, nothing the GC can move):
    //   0  int   file descriptor of the marker file
    //   4  int   1 once the crash was already reported by a managed handler
    //   8  int   signal number      \ the 16-byte record
    //  16  long  time(NULL)         / written to the marker file
    //  24  int[SignalCount]         hooked signal numbers
    //  40  sigaction[SignalCount]   the handlers that were installed before ours
    private const int FdOffset = 0, ReportedOffset = 4, RecordOffset = 8, TimeOffset = 16,
        SignalsOffset = 24, PreviousOffset = 40;
    private const int RecordSize = 16;
    private const int StateSize = PreviousOffset + SignalCount * SigactionSize;

    private static byte* s_state;
    private static SafeFileHandle? s_marker; // keeps the descriptor open for the process lifetime
    private static string? s_directory;

    /// <summary>A fatal signal recorded by an earlier run.</summary>
    internal readonly record struct PreviousCrash(int Signal, DateTime OccurredAt);

    /// <summary>Reads (and consumes) the record left by the previous run, if any.</summary>
    internal static PreviousCrash? TakePrevious(string directory)
    {
        try
        {
            var path = Path.Combine(directory, MarkerFile);
            if (!File.Exists(path))
                return null;

            var bytes = File.ReadAllBytes(path);
            File.Delete(path);
            if (bytes.Length < RecordSize)
                return null;

            var signal = BitConverter.ToInt32(bytes, 0);
            var seconds = BitConverter.ToInt64(bytes, TimeOffset - RecordOffset);
            if (signal <= 0 || seconds <= 0)
                return null;

            return new PreviousCrash(signal, DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Opens the marker file and installs the signal handlers. Safe to call once.</summary>
    internal static void Install(string directory)
    {
        if (s_state is not null)
            return;

        try
        {
            s_directory = directory;
            s_marker = File.OpenHandle(Path.Combine(directory, MarkerFile), FileMode.Create, FileAccess.Write, FileShare.ReadWrite);

            var state = (byte*)NativeMemory.AllocZeroed(StateSize);
            *(int*)(state + FdOffset) = (int)s_marker.DangerousGetHandle();

            ReadOnlySpan<int> signals = [SIGABRT, SIGILL, SIGTRAP, SIGSYS];
            for (var i = 0; i < SignalCount; i++)
                *(int*)(state + SignalsOffset + i * sizeof(int)) = signals[i];

            // Publish the block before any handler can run.
            s_state = state;

            var action = stackalloc byte[SigactionSize];
            new Span<byte>(action, SigactionSize).Clear();
            *(nint*)action = (nint)(delegate* unmanaged[Cdecl]<int, void>)&OnSignal;

            for (var i = 0; i < SignalCount; i++)
                sigaction(signals[i], action, state + PreviousOffset + i * SigactionSize);
        }
        catch
        {
            // Best effort: without the marker, native signals simply go unreported.
        }
    }

    /// <summary>
    /// Called when a managed handler has captured the crash: the abort() that follows
    /// must not be reported a second time as a bare SIGABRT.
    /// </summary>
    internal static void MarkReported()
    {
        var state = s_state;
        if (state is not null)
            *(int*)(state + ReportedOffset) = 1;
    }

    /// <summary>
    /// Keeps a snapshot of the live session next to the marker, so a signal can be
    /// attributed to it (and the session closed as crashed) on the next launch.
    /// </summary>
    internal static void RememberSession(SessionInfo? session)
    {
        var directory = s_directory;
        if (directory is null || session is null)
            return;

        try
        {
            var line = string.Join('|',
                session.SessionId.ToString("N"),
                session.Started.Ticks.ToString(CultureInfo.InvariantCulture),
                session.Errors.ToString(CultureInfo.InvariantCulture),
                session.DurationSeconds.ToString("R", CultureInfo.InvariantCulture),
                session.Sequence.ToString(CultureInfo.InvariantCulture),
                session.DistinctId);
            File.WriteAllText(Path.Combine(directory, SessionFile), line);
        }
        catch
        {
            // Best effort: the crash is still reported, just without its session.
        }
    }

    /// <summary>Reads (and consumes) the session snapshot of the previous run.</summary>
    internal static SessionInfo? TakePreviousSession(string directory)
    {
        try
        {
            var path = Path.Combine(directory, SessionFile);
            if (!File.Exists(path))
                return null;

            // The installation id goes last because it is the only free-form field.
            var parts = File.ReadAllText(path).Split('|', 6);
            File.Delete(path);
            if (parts.Length < 6)
                return null;

            return new SessionInfo
            {
                SessionId = Guid.ParseExact(parts[0], "N"),
                Started = new DateTime(long.Parse(parts[1], CultureInfo.InvariantCulture), DateTimeKind.Utc),
                Errors = int.Parse(parts[2], CultureInfo.InvariantCulture),
                DurationSeconds = double.Parse(parts[3], CultureInfo.InvariantCulture),
                Sequence = long.Parse(parts[4], CultureInfo.InvariantCulture),
                DistinctId = parts[5]
            };
        }
        catch
        {
            return null;
        }
    }

    internal static string Name(int signal) => signal switch
    {
        SIGABRT => "SIGABRT",
        SIGILL => "SIGILL",
        SIGTRAP => "SIGTRAP",
        SIGSYS => "SIGSYS",
        _ => "SIG" + signal.ToString(CultureInfo.InvariantCulture)
    };

    // Runs inside the signal: only reads/writes the native block and calls
    // async-signal-safe libc functions. No allocation, no locks, no managed objects.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnSignal(int signal)
    {
        var state = s_state;
        if (state is not null)
        {
            if (*(int*)(state + ReportedOffset) == 0)
            {
                *(int*)(state + RecordOffset) = signal;
                *(long*)(state + TimeOffset) = time(null);
                write(*(int*)(state + FdOffset), state + RecordOffset, RecordSize);
                // One record is enough, whatever else fires while the process dies.
                *(int*)(state + ReportedOffset) = 1;
            }

            // Put back the handler we replaced (the runtime's, or the default).
            for (var i = 0; i < SignalCount; i++)
            {
                if (*(int*)(state + SignalsOffset + i * sizeof(int)) == signal)
                    sigaction(signal, state + PreviousOffset + i * SigactionSize, null);
            }
        }

        // The signal is blocked while we are in here; re-raised, it is delivered to
        // the restored handler as soon as we return, so the crash proceeds as usual.
        raise(signal);
    }

    [DllImport("libc", EntryPoint = "sigaction"), SuppressGCTransition]
    private static extern int sigaction(int signal, byte* action, byte* previous);

    [DllImport("libc", EntryPoint = "write"), SuppressGCTransition]
    private static extern nint write(int descriptor, byte* buffer, nint count);

    [DllImport("libc", EntryPoint = "time"), SuppressGCTransition]
    private static extern long time(long* result);

    [DllImport("libc", EntryPoint = "raise"), SuppressGCTransition]
    private static extern int raise(int signal);
}
#endif
