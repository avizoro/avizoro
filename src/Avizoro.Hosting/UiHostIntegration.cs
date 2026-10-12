using Avizoro.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Avizoro.Hosting;

/// <summary>ANR bridge for UI frameworks that expose a <see cref="SynchronizationContext"/> for their UI thread.</summary>
public sealed class SynchronizationContextDispatcher : IUiThreadDispatcher
{
    private readonly SynchronizationContext _context;

    public SynchronizationContextDispatcher(SynchronizationContext context) => _context = context;

    public void Post(Action action) => _context.Post(_ => action(), null);

    /// <summary>.NET can't read another managed thread's stack; the event still records the freeze.</summary>
    public string? CaptureUiThreadStack() => null;
}

/// <summary>Shared wiring for desktop UI SDKs (WPF, WinForms, Avalonia).</summary>
public static class UiHostIntegration
{
    /// <summary>
    /// Registers the frozen-UI watchdog for the UI thread behind <paramref name="uiContext"/>
    /// (when ANR detection is enabled). Call from the UI thread during startup.
    /// </summary>
    public static void AddUiThreadWatchdog(IServiceCollection services, AvizoroHostOptions options, SynchronizationContext? uiContext)
    {
        if (!options.EnableAnrDetection || uiContext is null)
            return;

        services.TryAddSingleton<IUiThreadDispatcher>(new SynchronizationContextDispatcher(uiContext));
        services.TryAddSingleton<UiThreadWatchdog>();
    }

    /// <summary>
    /// Handles an exception a UI dispatcher reported as unhandled. If the app
    /// survives it (a handler marked it handled), a background-priority callback
    /// runs and records it as a handled error; if it takes the process down, that
    /// callback never runs and the fatal <c>AppDomain</c> hook records the crash
    /// instead — so it's reported exactly once either way.
    /// </summary>
    public static void OnDispatcherException(Exception exception, Action<Action> postLowPriority)
    {
        var reporter = (AvizoroSdk.Instance?.Services.GetService(typeof(ManagedCrashReporter))) as ManagedCrashReporter;
        if (reporter is null)
            return;

        try
        {
            postLowPriority(() =>
            {
                if (!reporter.FatalCaptured)
                    reporter.CaptureHandled(exception);
            });
        }
        catch
        {
        }
    }
}
