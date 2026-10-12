# Avizoro.Core

Framework-agnostic core of **Avizoro**, a self-hosted crash-reporting &
observability stack for .NET (a Sentry/Crashlytics alternative for the .NET
ecosystem).

This package holds the domain model (`AvizoroEvent`, `ExceptionInfo`,
`Breadcrumb`, `DeviceRuntime`, sessions, performance spans, user feedback), the
enums, and the service layer every SDK shares:

- `AvizoroService`: the single funnel that enriches every event, applies
  `BeforeSend`, sampling and PII scrubbing, and does store-and-forward.
- `SessionTracker`, `PerformanceMonitor`, `UiThreadWatchdog` (ANR detection),
  `AvizoroHttpHandler` (HTTP breadcrumbs and timings) and `AvizoroScope`.
- The MSBuild target that uploads PDBs and Android mapping files after a Release
  build (`AvizoroUploadSymbols=true`).

It has **no UI framework dependency**. You normally get it through a platform package:

| App type | Package |
|---|---|
| .NET MAUI | [`Avizoro.Maui`](https://www.nuget.org/packages/Avizoro.Maui) |
| ASP.NET Core | [`Avizoro.AspNetCore`](https://www.nuget.org/packages/Avizoro.AspNetCore) |
| Blazor WebAssembly | [`Avizoro.Blazor`](https://www.nuget.org/packages/Avizoro.Blazor) |
| WPF / WinForms / Avalonia | [`Avizoro.Wpf`](https://www.nuget.org/packages/Avizoro.Wpf) / [`.WinForms`](https://www.nuget.org/packages/Avizoro.WinForms) / [`.Avalonia`](https://www.nuget.org/packages/Avizoro.Avalonia) |
| Console, workers, services | [`Avizoro.Hosting`](https://www.nuget.org/packages/Avizoro.Hosting) |

See the [documentation](https://wilsonvargas.github.io/Avizoro/) for the
server, dashboard and every SDK.
