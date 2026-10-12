using System.Diagnostics;
using Avizoro.Core.Configuration;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Providers;
using Avizoro.Core.Storage;

namespace Avizoro.Core.Services;

/// <summary>Collects performance spans and ships them in batches.</summary>
public interface IPerformanceMonitor
{
    /// <summary>Records a finished span (subject to <c>PerformanceSampleRate</c>).</summary>
    void Record(PerformanceSpan span);

    /// <summary>Starts timing an operation; disposing the handle records it.</summary>
    SpanHandle StartSpan(string operation, string name);

    /// <summary>Ships buffered spans now (e.g. when the app goes to the background).</summary>
    Task FlushAsync();
}

/// <summary>A running span. Dispose (or call <see cref="Finish"/>) to record it.</summary>
public sealed class SpanHandle : IDisposable
{
    private readonly IPerformanceMonitor _monitor;
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private int _finished;

    internal SpanHandle(IPerformanceMonitor monitor, string operation, string name)
    {
        _monitor = monitor;
        Span = new PerformanceSpan { Operation = operation, Name = name, Start = DateTime.UtcNow };
    }

    public PerformanceSpan Span { get; }

    public void Finish(bool success = true, int? statusCode = null)
    {
        if (Interlocked.Exchange(ref _finished, 1) != 0)
            return;

        Span.DurationMs = Math.Round(_watch.Elapsed.TotalMilliseconds, 3);
        Span.Success = success;
        Span.StatusCode = statusCode;
        _monitor.Record(Span);
    }

    public void Dispose() => Finish();
}

/// <summary>
/// Default <see cref="IPerformanceMonitor"/>: buffers spans in memory and sends
/// them as one <see cref="EventType.Performance"/> event through the
/// store-and-forward queue every <see cref="AvizoroOptions.PerformanceFlushInterval"/>
/// or once <see cref="BatchSize"/> spans are waiting. Spans are telemetry, not
/// errors: they skip BeforeSend/breadcrumbs, and losing an unflushed batch in a
/// crash is acceptable.
/// </summary>
public sealed class PerformanceMonitor : IPerformanceMonitor, IDisposable
{
    public const int BatchSize = 50;

    private readonly IEventStore _store;
    private readonly IEventUploader _uploader;
    private readonly AvizoroOptions _options;
    private readonly IDeviceContextProvider _device;
    private readonly List<PerformanceSpan> _buffer = new();
    private readonly object _gate = new();
    private readonly Timer? _timer;

    public PerformanceMonitor(IEventStore store, IEventUploader uploader, AvizoroOptions options, IDeviceContextProvider device)
    {
        _store = store;
        _uploader = uploader;
        _options = options;
        _device = device;

        var interval = options.PerformanceFlushInterval;
        if (interval > TimeSpan.Zero)
            _timer = new Timer(_ => _ = FlushAsync(), null, interval, interval);
    }

    public int Pending
    {
        get { lock (_gate) return _buffer.Count; }
    }

    public void Record(PerformanceSpan span)
    {
        var rate = _options.PerformanceSampleRate;
        if (rate < 1.0 && (rate <= 0.0 || Random.Shared.NextDouble() >= rate))
            return;

        bool full;
        lock (_gate)
        {
            _buffer.Add(span);
            full = _buffer.Count >= BatchSize;
        }

        if (full)
            _ = FlushAsync();
    }

    public SpanHandle StartSpan(string operation, string name) => new(this, operation, name);

    public async Task FlushAsync()
    {
        List<PerformanceSpan> batch;
        lock (_gate)
        {
            if (_buffer.Count == 0)
                return;
            batch = new List<PerformanceSpan>(_buffer);
            _buffer.Clear();
        }

        var e = new AvizoroEvent { EventType = EventType.Performance, Spans = batch, Timestamp = DateTime.UtcNow };
        EventContext.Stamp(e, _options, _device);

        try
        {
            await _store.SaveAsync(e).ConfigureAwait(false);
            await _uploader.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best-effort telemetry.
        }
    }

    public void Dispose() => _timer?.Dispose();
}
