---
title: SDKs
nav_order: 3
---

# SDKs
{: .no_toc }

Every SDK uses the same DSN, sends the same event format and shares the options in [`AvizoroOptions`](#common-options).

1. TOC
{:toc}

| Package | For | Entry point |
|---|---|---|
| `Avizoro.Maui` | .NET MAUI (Android, iOS, Mac Catalyst, Windows) | `builder.UseAvizoro(dsn)` |
| `Avizoro.AspNetCore` | ASP.NET Core APIs and sites | `builder.UseAvizoro(dsn)` |
| `Avizoro.Blazor` | Blazor WebAssembly | `builder.UseAvizoro(dsn)` + `host.StartAvizoro()` |
| `Avizoro.Wpf` | WPF | `AvizoroWpf.Init(dsn)` |
| `Avizoro.WinForms` | Windows Forms | `AvizoroWinForms.Init(dsn)` |
| `Avizoro.Avalonia` | Avalonia desktop | `AppBuilder.UseAvizoro(dsn)` |
| `Avizoro.Hosting` | Console apps, workers, services | `services.AddAvizoro(dsn)` or `AvizoroSdk.Init(dsn)` |
| `Avizoro.Extensions.Logging` | Any app using `ILogger` | `logging.AddAvizoro()` |
| `Avizoro.Core` | Building your own integration | Domain model and services |

## .NET MAUI

```csharp
builder
    .UseMauiApp<App>()
    .UseAvizoro("https://projectKey@host/my-app", o =>
    {
        o.Environment = "production";
        o.EnableScreenCapture = true;              // opt-in, downscaled JPEG
        o.ScreenCaptureExcludedPages.Add("LoginPage");
        o.EnableCrashFeedbackPrompt = true;        // ask the user after a crash
        o.EnableSessionTimeline = true;            // opt-in, every session as a timeline
    });

builder.Services.AddHttpClient("api")
    .AddAvizoroHandler();                  // HTTP breadcrumbs + timings
```

What the MAUI SDK captures:

- **Fatal crashes**: managed exceptions on every platform, and Java exceptions on Android. The crash is written to the local queue and sent on the next launch.
- **ANRs**: the UI thread blocked for longer than `AnrThreshold` (5 s by default).
- **Sessions**: one per foreground period. The app going to the background for longer than `SessionTimeout` ends the session.
- **Performance**: cold app start, page load per page type, and HTTP requests through the handler.
- **Breadcrumbs**: navigation, `ILogger` output, HTTP calls and lifecycle events.
- **Device state**: memory, battery, network and the current screen at the time of the event.
- **Session timeline** (opt-in): the whole session, not only what led to an error. Add a screenshot of the
  current screen with `IScreenCapture.CaptureToTimelineAsync()` (inject `IScreenCapture`).

Android, iOS, Mac Catalyst and Windows are supported. Crash capture on iOS and Mac Catalyst covers managed exceptions and uncaught Objective-C exceptions (`NSException`, with its native stack). A fatal signal (`SIGABRT`, `SIGILL`, `SIGTRAP`, `SIGSYS`) is reported on the next launch without a stack trace; `SIGSEGV` and `SIGBUS` are not captured, because the .NET runtime owns them. Turn all of this off with `EnableNativeCrashCapture = false`.

## ASP.NET Core

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.UseAvizoro("https://projectKey@host/my-api", o =>
{
    o.SampleRate = 0.5;           // keep half of the handled errors
});

builder.Services.AddHttpClient<PaymentsClient>()
    .AddAvizoroHandler(); // trace outgoing calls
```

- Exceptions that escape the request pipeline are captured and tagged with the method, route, path and trace id.
- `ILogger` errors become events, and lower-level logs become breadcrumbs. The trail is **scoped to each request**, so concurrent requests never mix breadcrumbs.
- Every request is timed as an `http.server` span per route template (`GET /orders/{id}`).
- An exception that is both logged and thrown is reported once.

## Blazor WebAssembly

```csharp
var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.UseAvizoro("https://projectKey@host/my-spa");

var host = builder.Build();
host.StartAvizoro();
await host.RunAsync();
```

Unhandled component exceptions and `ILogger` errors become events. Route changes become breadcrumbs. The browser has no durable storage for the SDK, so the queue is kept in memory and session tracking is off. The server must allow your app's origin in CORS. The default policy allows any origin.

## WPF

```csharp
public partial class App : Application
{
    public App() => AvizoroWpf.Init("https://projectKey@host/my-desktop-app");
}
```

## Windows Forms

```csharp
[STAThread]
static void Main()
{
    using var ik = AvizoroWinForms.Init("https://projectKey@host/my-winforms-app");
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
}
```

By default an unhandled UI-thread exception ends the app and is captured as a crash, instead of showing the WinForms "continue?" dialog. Pass `crashOnUnhandledUiException: false` to keep the app running and report the exception as a handled error.

## Avalonia

```csharp
public static AppBuilder BuildAvaloniaApp()
    => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .UseAvizoro("https://projectKey@host/my-avalonia-app");
```

The three desktop SDKs all provide:

- Fatal crashes, queued locally and sent on the next launch.
- Dispatcher exceptions the app survives, reported as handled errors and never counted twice.
- ANR detection for a frozen UI thread.
- Window breadcrumbs and `ILogger` errors.
- One session per app run.
- A flush on exit.

Use `AvizoroSdk.Current` anywhere to report exceptions, logs, users and tags.

## Console apps, workers and services

With the generic host:

```csharp
builder.Services.AddAvizoro("https://projectKey@host/my-worker");
```

Without DI:

```csharp
using var ik = AvizoroSdk.Init("https://projectKey@host/my-tool");
await AvizoroSdk.Current!.TrackLogAsync(SeverityLevel.Warning, "Low disk space");
```

The offline queue lives in `%LOCALAPPDATA%/Avizoro/{projectId}`. Set `OfflineStorePath` to change it. On shutdown the SDK waits up to `ShutdownFlushTimeout` (5 s) for pending uploads.

To tag everything reported during one unit of work (a job or a message), push a scope:

```csharp
using (var scope = AvizoroScope.Push())
{
    scope.SetTag("job", job.Id);
    await RunAsync(job);        // events captured here carry the tag and the scope's breadcrumbs
}
```

## ILogger provider

MAUI, ASP.NET Core, Blazor and the desktop SDKs register this provider for you. To add it yourself:

```csharp
builder.Logging.AddAvizoro(o =>
{
    o.MinimumBreadcrumbLevel = LogLevel.Information;   // becomes the breadcrumb trail
    o.MinimumEventLevel = LogLevel.Error;              // sent as events
});
```

A log event without an exception is grouped by its **message template**, so `"Order {Id} failed"` is one issue rather than one issue per id. Structured properties are stored in the event data.

## Common options

These apply to every SDK (`AvizoroOptions`). The desktop, hosting and ASP.NET Core SDKs add `OfflineStorePath`, `UseInMemoryStore`, `EnableCrashHandlers` and `ShutdownFlushTimeout`.

| Option | Default | What it does |
|---|---|---|
| `Environment` | `production` | Attached to every event, filterable in the dashboard. |
| `Release` | app version | Used for release health, regressions and symbol lookup. |
| `EnableCrashReporting` | `true` | Install the platform crash handlers. |
| `EnableAutoSessionTracking` | `true` | Sessions for crash-free rates. |
| `SessionTimeout` | 30 s | How long the app can stay in the background before a new session starts. |
| `EnableSessionTimeline` | `false` | Record every session as a timeline (screens, actions, HTTP, logs, screenshots). Needs session tracking. MAUI and the desktop/hosting SDKs. |
| `SessionTimelineMaxEntries` / `SessionTimelineSampleRate` | `1000` / `1.0` | Entries recorded per session, and fraction of sessions recorded. |
| `SessionTimelineFlushInterval` | 5 s | How often recorded entries are queued and sent. |
| `EnableAnrDetection` / `AnrThreshold` | `true` / 5 s | Report a blocked UI thread. |
| `EnableNativeCrashCapture` | `true` | iOS and Mac Catalyst: uncaught `NSException` and fatal-signal markers. |
| `EnablePerformanceMonitoring` | `true` | App start, page load and HTTP spans. |
| `PerformanceSampleRate` | `1.0` | Fraction of spans to keep. |
| `SampleRate` | `1.0` | Fraction of **non-fatal** events to keep. Crashes are always kept. |
| `BeforeSend` | none | Change or drop (return `null`) any event before it is stored. |
| `BeforeBreadcrumb` | none | Change or drop a breadcrumb. |
| `EnablePiiScrubbing` | `true` | Mask emails, tokens, card numbers and sensitive keys. |
| `ScrubbingSensitiveKeys` / `ScrubbingPatterns` | empty | Your own keys and regex patterns to mask. |
| `BreadcrumbCapacity` | `50` | Size of the breadcrumb ring buffer. |
| `EnableScreenCapture` | `false` | Last-screen capture (MAUI). |
| `EnableCrashFeedbackPrompt` | `false` | Ask for comments on the next launch after a crash (MAUI). |
| `EnableLoggingIntegration` | `true` | Register the `ILogger` provider. |

## The IAvizoro API

```csharp
Task TrackExceptionAsync(Exception exception);
Task TrackLogAsync(SeverityLevel level, string message, IDictionary<string, string>? data = null);
Task TrackAsync(AvizoroEvent avizoroEvent);   // full control, e.g. a custom Fingerprint
void AddBreadcrumb(string message, string category = BreadcrumbCategories.Custom,
    SeverityLevel level = SeverityLevel.Information, IDictionary<string, string>? data = null);
void SetUser(string? userId);
void SetTag(string key, string? value);
Task CaptureFeedbackAsync(UserFeedback feedback);
Guid? LastEventId { get; }

// Extension methods (AvizoroExtensions)
void TrackAction(string action, IDictionary<string, string>? data = null);  // "user" breadcrumb
void TrackScreen(string screen, IDictionary<string, string>? data = null);  // "navigation" breadcrumb
```

Pages are tracked automatically in MAUI; use `TrackScreen` for tabs, popups or custom views.

Inject `IPerformanceMonitor` to time your own operations:

```csharp
using (performance.StartSpan("db.query", "LoadCatalog"))
{
    await LoadCatalogAsync();
}
```
