# Avizoro.Hosting

Generic [Avizoro](https://github.com/avizoro/avizoro) SDK for any
.NET app: console tools, workers, services, desktop apps. It is also the base of the
`Avizoro.AspNetCore`, `.Wpf`, `.WinForms`, `.Avalonia` and `.Blazor` packages.

With the generic host / DI:

```csharp
builder.Services.AddAvizoro("http://projectKey@host:7099/my-worker");
```

Without DI:

```csharp
using var ik = AvizoroSdk.Init("http://projectKey@host:7099/my-tool");
await AvizoroSdk.Current!.TrackLogAsync(SeverityLevel.Warning, "Low disk space");
```

What you get:

- Fatal crashes (`AppDomain.UnhandledException`) are written to a local queue and
  sent on the next start; unobserved task exceptions are reported as handled errors.
- Offline store-and-forward in `%LOCALAPPDATA%/Avizoro/{projectId}`
  (`OfflineStorePath` to change it; in memory in the browser).
- `ILogger` integration, sessions, and performance spans (`AddAvizoroHandler()` on
  `HttpClient`s).
- `AvizoroScope.Push()` for per-operation tags/breadcrumbs (per request, per job).
- A flush on shutdown, bounded by `ShutdownFlushTimeout`.
