using IntelligenceKit.Core.Configuration;
using IntelligenceKit.Core.Models;
using IntelligenceKit.Core.Services;

namespace IntelligenceKit.Core.Diagnostics;

/// <summary>
/// Thread-safe fixed-capacity breadcrumb buffer. Capacity comes from
/// <see cref="IntelligenceOptions.BreadcrumbCapacity"/>.
/// </summary>
public class BreadcrumbBuffer : IBreadcrumbBuffer
{
    private readonly int _capacity;
    private readonly LinkedList<Breadcrumb> _items = new();
    private readonly object _lock = new();
    private readonly Func<Breadcrumb, Breadcrumb?>? _beforeBreadcrumb;
    private readonly ISessionTimeline? _timeline;

    public BreadcrumbBuffer(IntelligenceOptions options)
        : this(options, timeline: null)
    {
    }

    /// <summary>
    /// With a <paramref name="timeline"/>, every breadcrumb that enters the trail is also
    /// recorded on the current session's timeline (after <c>BeforeBreadcrumb</c>).
    /// </summary>
    public BreadcrumbBuffer(IntelligenceOptions options, ISessionTimeline? timeline)
    {
        _capacity = Math.Max(1, options.BreadcrumbCapacity);
        _beforeBreadcrumb = options.BeforeBreadcrumb;
        _timeline = timeline;
    }

    public void Add(Breadcrumb breadcrumb)
    {
        if (_beforeBreadcrumb is not null)
        {
            try
            {
                var kept = _beforeBreadcrumb(breadcrumb);
                if (kept is null)
                    return;
                breadcrumb = kept;
            }
            catch
            {
                // A faulty hook must not lose the breadcrumb or break the caller.
            }
        }

        lock (_lock)
        {
            _items.AddLast(breadcrumb);
            while (_items.Count > _capacity)
                _items.RemoveFirst();
        }

        if (_timeline is not null)
        {
            try
            {
                _timeline.Record(breadcrumb);
            }
            catch
            {
                // The timeline is best-effort; the trail already has the breadcrumb.
            }
        }
    }

    public IReadOnlyList<Breadcrumb> Snapshot()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
    }
}
