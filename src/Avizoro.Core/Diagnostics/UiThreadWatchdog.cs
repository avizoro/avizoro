using System.Diagnostics;
using Avizoro.Core.Configuration;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;

namespace Avizoro.Core.Diagnostics;

/// <summary>
/// Detects a frozen UI ("Application Not Responding"): a dedicated background
/// thread keeps posting a no-op to the UI thread and waits for it to run. If it
/// hasn't run within <see cref="AvizoroOptions.AnrThreshold"/>, the UI thread
/// is blocked — the watchdog snapshots its stack (when the platform can) and
/// reports one <see cref="AnrExceptionType"/> event. It then waits for the UI to
/// recover before arming again, so one long freeze is one report.
///
/// If the watchdog thread itself overslept (the OS suspended the whole app, e.g.
/// in the background), the round is discarded rather than reported as a freeze.
/// </summary>
public sealed class UiThreadWatchdog : IDisposable
{
    /// <summary>Exception type of ANR events (also what they group under).</summary>
    public const string AnrExceptionType = "ApplicationNotResponding";

    /// <summary>Delay between two healthy pings.</summary>
    private static readonly TimeSpan PingInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>Extra slack before a missed deadline is blamed on app suspension.</summary>
    private static readonly TimeSpan SuspensionSlack = TimeSpan.FromSeconds(2);

    private readonly IAvizoro _kit;
    private readonly IUiThreadDispatcher _dispatcher;
    private readonly TimeSpan _threshold;
    private readonly ManualResetEventSlim _running = new(false);
    private readonly CancellationTokenSource _disposed = new();
    private Thread? _thread;

    public UiThreadWatchdog(IAvizoro kit, IUiThreadDispatcher dispatcher, AvizoroOptions options)
    {
        _kit = kit;
        _dispatcher = dispatcher;
        _threshold = options.AnrThreshold <= TimeSpan.Zero ? TimeSpan.FromSeconds(5) : options.AnrThreshold;
    }

    /// <summary>Number of freezes reported so far (diagnostics/tests).</summary>
    public int ReportedCount { get; private set; }

    /// <summary>Starts (or resumes) monitoring.</summary>
    public void Start()
    {
        if (_disposed.IsCancellationRequested)
            return;

        lock (_running)
        {
            if (_thread is null)
            {
                _thread = new Thread(Run) { IsBackground = true, Name = "Avizoro.UiWatchdog" };
                _thread.Start();
            }
        }

        _running.Set();
    }

    /// <summary>Pauses monitoring (e.g. while the app is in the background).</summary>
    public void Pause() => _running.Reset();

    public void Dispose()
    {
        _disposed.Cancel();
        _running.Set(); // release a paused loop so it can exit
    }

    private void Run()
    {
        var token = _disposed.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                _running.Wait(token);
                RunOneRound(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Never let the watchdog crash the app; back off and keep going.
                token.WaitHandle.WaitOne(PingInterval);
            }
        }
    }

    private void RunOneRound(CancellationToken token)
    {
        using var answered = new ManualResetEventSlim(false);
        var watch = Stopwatch.StartNew();
        _dispatcher.Post(() =>
        {
            try { answered.Set(); } catch (ObjectDisposedException) { }
        });

        if (answered.Wait(_threshold, token))
        {
            token.WaitHandle.WaitOne(PingInterval);
            return;
        }

        // Missed the deadline. If this thread itself woke far too late, the app was
        // most likely suspended — not frozen. Skip this round.
        if (watch.Elapsed > _threshold + SuspensionSlack || !_running.IsSet)
        {
            answered.Wait(token);
            return;
        }

        // Genuinely blocked: capture the stack WHILE it is blocked, then report.
        string? stack = null;
        try
        {
            stack = _dispatcher.CaptureUiThreadStack();
        }
        catch
        {
        }

        Report(stack);

        // One report per freeze: wait for the UI thread to come back before re-arming.
        answered.Wait(token);
    }

    private void Report(string? stack)
    {
        ReportedCount++;
        var anr = new AvizoroEvent
        {
            EventType = EventType.Exception,
            Level = SeverityLevel.Error,
            Exception = new ExceptionInfo
            {
                Type = AnrExceptionType,
                Message = $"The UI thread was blocked for more than {_threshold.TotalSeconds:0.#} s.",
                StackTrace = stack ?? string.Empty,
                Source = "Avizoro.UiThreadWatchdog"
            },
            Tags = { ["mechanism"] = "anr" }
        };

        // Fire-and-forget: the event is persisted first (store-and-forward) and the
        // watchdog thread must not block on the network.
        _ = SafeTrackAsync(anr);
    }

    private async Task SafeTrackAsync(AvizoroEvent anr)
    {
        try
        {
            await _kit.TrackAsync(anr).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}
