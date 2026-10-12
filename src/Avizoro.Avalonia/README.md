# Avizoro.Avalonia

Crash reporting and diagnostics for **Avalonia** desktop apps with a self-hosted
[Avizoro](https://github.com/wilsonvargas/Avizoro) server.

```csharp
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvizoro("http://projectKey@host:7099/my-avalonia-app");
```

- Fatal crashes are written to a local queue and sent on the next launch; dispatcher
  exceptions the app survives are reported as handled errors (never double-counted).
- A frozen UI thread (default 5 s) is reported as `ApplicationNotResponding`.
- Window breadcrumbs, `ILogger` errors, one session per app run, and a flush on exit.
