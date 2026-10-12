using System.Windows;
using System.Windows.Threading;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Hosting;

namespace Avizoro.Wpf;

/// <summary>
/// Avizoro for WPF. Call once at startup (e.g. in <c>App</c>'s constructor
/// or <c>OnStartup</c>):
/// <code>AvizoroWpf.Init("http://key@host:7099/my-desktop-app");</code>
/// Captures fatal crashes (persisted, sent on next launch), dispatcher exceptions
/// the app survives, a frozen UI thread (ANR), window breadcrumbs, logged errors
/// and one session per app run. Everything is flushed when the app exits.
/// </summary>
public static class AvizoroWpf
{
    public static AvizoroInstance Init(string dsn, Action<AvizoroHostOptions>? configure = null)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("AvizoroWpf.Init must be called after the WPF Application is created (e.g. in App's constructor or OnStartup).");

        var instance = AvizoroSdk.Init(dsn, configure, (services, options) =>
            UiHostIntegration.AddUiThreadWatchdog(services, options, new DispatcherSynchronizationContext(app.Dispatcher)));

        app.DispatcherUnhandledException += (_, e) =>
            UiHostIntegration.OnDispatcherException(e.Exception,
                action => app.Dispatcher.BeginInvoke(DispatcherPriority.Background, action));

        // A breadcrumb for every window that opens.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => WindowBreadcrumb(instance, sender)));

        app.Exit += (_, _) => instance.Dispose();
        return instance;
    }

    private static void WindowBreadcrumb(AvizoroInstance instance, object sender)
    {
        try
        {
            instance.Kit.AddBreadcrumb(sender.GetType().Name, BreadcrumbCategories.Navigation, SeverityLevel.Information);
        }
        catch
        {
        }
    }
}
