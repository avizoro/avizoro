# Avizoro.Wpf

Crash reporting and diagnostics for **WPF** apps with a self-hosted
[Avizoro](https://github.com/avizoro/avizoro) server.

```csharp
public partial class App : Application
{
    public App() => AvizoroWpf.Init("http://projectKey@host:7099/my-desktop-app");
}
```

- Fatal crashes are written to a local queue and sent on the next launch.
- Dispatcher exceptions the app survives (a handler set `Handled = true`) are
  reported as handled errors, and never double-counted with crashes.
- A frozen UI thread (default 5 s) is reported as `ApplicationNotResponding`.
- Window breadcrumbs, `ILogger` errors, one session per app run, and a flush on exit.

Use `AvizoroSdk.Current` anywhere to track exceptions, logs, users and tags.
