# Avizoro.Maui

**One-line, self-hosted crash reporting & observability for .NET MAUI** on Android,
iOS, Mac Catalyst and Windows — a Sentry/App Center/Crashlytics alternative for the
.NET ecosystem. Ships crashes, ANRs, logs, sessions and performance to a backend you
control, viewable on a real-time dashboard.

## Install

```bash
dotnet add package Avizoro.Maui
```

## Use

A single line in `MauiProgram.cs` wires up everything:

```csharp
builder
    .UseMauiApp<App>()
    .UseAvizoro("http://projectKey@your-server:7099/my-app", o =>
    {
        o.Environment = "production";
        o.EnableCrashFeedbackPrompt = true;   // optional: ask what happened after a crash
    });

builder.Services.AddHttpClient("api").AddAvizoroHandler();   // HTTP breadcrumbs + timings
```

That one call turns on:

- **Crash capture** (managed and, on Android, Java exceptions), queued offline and sent on the next launch.
- **ANR detection** when the UI thread is frozen for more than 5 s.
- **Sessions** for crash-free rates and release health.
- **Performance**: cold app start, page load per page, and HTTP requests.
- **Breadcrumbs** from navigation, `ILogger`, HTTP and app lifecycle, plus device state.
- **PII scrubbing** by default, with `BeforeSend`, `BeforeBreadcrumb` and `SampleRate`.
- **Last-screen capture** (opt-in).

```csharp
// IAvizoro is injected via DI
kit.SetUser("anon-123");
kit.SetTag("plan", "premium");
kit.AddBreadcrumb("Tapped Checkout");
await kit.TrackLogAsync(SeverityLevel.Warning, "Cart total mismatch");
await kit.TrackExceptionAsync(ex);   // manual capture — crashes are automatic
```

> **Android emulator:** use the host alias `10.0.2.2` instead of `localhost` in the DSN.

You'll need the [Avizoro server + dashboard](https://github.com/avizoro/avizoro)
running to receive and view events. Full guide: [documentation](https://avizoro.github.io/avizoro/sdks#net-maui).
