using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace Avizoro.Maui.Diagnostics;

/// <summary>
/// Records a navigation breadcrumb every time a page appears and remembers the
/// current screen so the runtime snapshot can report it. This is what produces
/// the "Previous Actions" trail leading up to a crash.
///
/// With performance monitoring on, it also times the app start (process start —
/// or SDK init where the platform can't tell — to the first page) and every page
/// load (the previous page disappearing to the next one appearing).
/// </summary>
public sealed class NavigationTracker
{
    private readonly IBreadcrumbBuffer _breadcrumbs;
    private readonly AvizoroOptions _options;
    private readonly IPerformanceMonitor? _performance;
    private readonly System.Diagnostics.Stopwatch _sinceInit = System.Diagnostics.Stopwatch.StartNew();
    private bool _started;
    private bool _firstPageSeen;
    private long? _navigationStartTicks;

    public NavigationTracker(IBreadcrumbBuffer breadcrumbs, AvizoroOptions options)
        : this(breadcrumbs, options, performance: null)
    {
    }

    public NavigationTracker(IBreadcrumbBuffer breadcrumbs, AvizoroOptions options, IPerformanceMonitor? performance)
    {
        _breadcrumbs = breadcrumbs;
        _options = options;
        _performance = performance;
    }

    /// <summary>The page the user is currently on, if known.</summary>
    public string? CurrentScreen { get; private set; }

    public void Start()
    {
        if (_started)
            return;

        _started = true;

        // Initialize() runs before the App exists (Application.Current is null),
        // so defer the subscription until it becomes available.
        _ = SubscribeWhenReadyAsync();
    }

    private async Task SubscribeWhenReadyAsync()
    {
        for (var i = 0; i < 100 && Application.Current is null; i++)
            await Task.Delay(100);

        var app = Application.Current;
        if (app is null)
            return;

        app.PageAppearing += OnPageAppearing;
        app.PageDisappearing += OnPageDisappearing;

        // On a fast startup the first page can appear before we subscribe (we poll
        // for Application.Current); catch up so the first screen and the app-start
        // timing aren't lost. Skipped if PageAppearing already fired meanwhile.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (CurrentScreen is null && app.Windows.FirstOrDefault()?.Page is { } root)
                OnPageAppearing(app, VisiblePage(root));
        });
    }

    /// <summary>The page actually on screen inside a Shell/NavigationPage/FlyoutPage container.</summary>
    private static Page VisiblePage(Page root) => root switch
    {
        Shell { CurrentPage: { } current } => current,
        NavigationPage { CurrentPage: { } current } => current,
        FlyoutPage { Detail: { } detail } => VisiblePage(detail),
        _ => root
    };

    private void OnPageDisappearing(object? sender, Page page)
        => _navigationStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();

    private void OnPageAppearing(object? sender, Page page)
    {
        var screen = page.GetType().Name;
        CurrentScreen = screen;

        RecordTimings(screen);

        if (_options.EnableNavigationBreadcrumbs)
        {
            _breadcrumbs.Add(new Breadcrumb
            {
                Category = BreadcrumbCategories.Navigation,
                Message = screen,
                Level = SeverityLevel.Information
            });
        }
    }

    private void RecordTimings(string screen)
    {
        if (_performance is null)
            return;

        try
        {
            if (!_firstPageSeen)
            {
                _firstPageSeen = true;
                var (duration, kind) = AppStartDuration();
                _performance.Record(new PerformanceSpan
                {
                    Operation = SpanOperations.AppStart,
                    Name = kind,
                    Start = DateTime.UtcNow - duration,
                    DurationMs = Math.Round(duration.TotalMilliseconds, 1)
                });
                return;
            }

            if (_navigationStartTicks is { } start)
            {
                _navigationStartTicks = null;
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start);
                _performance.Record(new PerformanceSpan
                {
                    Operation = SpanOperations.PageLoad,
                    Name = screen,
                    Start = DateTime.UtcNow - elapsed,
                    DurationMs = Math.Round(elapsed.TotalMilliseconds, 1)
                });
            }
        }
        catch
        {
            // Timing is best-effort.
        }
    }

    /// <summary>
    /// Time from process start to now ("cold"), when the platform exposes the
    /// process start; otherwise from SDK initialization ("sdk-init").
    /// </summary>
    private (TimeSpan Duration, string Kind) AppStartDuration()
    {
        try
        {
#if ANDROID
            if (OperatingSystem.IsAndroidVersionAtLeast(24))
            {
                var ms = Android.OS.SystemClock.ElapsedRealtime() - Android.OS.Process.StartElapsedRealtime;
                if (ms > 0)
                    return (TimeSpan.FromMilliseconds(ms), "cold");
            }
#else
            var started = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
            var sinceProcess = DateTime.UtcNow - started;
            if (sinceProcess > TimeSpan.Zero && sinceProcess < TimeSpan.FromMinutes(5))
                return (sinceProcess, "cold");
#endif
        }
        catch
        {
            // Not available on this platform/version.
        }

        return (_sinceInit.Elapsed, "sdk-init");
    }
}
