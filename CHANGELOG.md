# Changelog

All notable changes to IntelligenceKit are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The public API of the `Avizoro.Core` and `Avizoro.Maui` NuGet packages
is what SemVer applies to (see [Versioning](README.md#versioning-and-api-stability)).

## [2.0.0] - 2026-10-12

**IntelligenceKit is now Avizoro.** This is a breaking release for apps that reference the SDK:
package IDs, namespaces and type names change. The wire protocol and stored data are compatible, so a
renamed server keeps accepting events from 1.x SDKs and keeps its database.

> **Upgrade the server first** if you turn on `EnableSessionTimeline`: an older server stores
> timeline batches as ordinary events. The server migrates its database on startup.

### Changed

- **Packages**: `IntelligenceKit.*` is published as `Avizoro.*` (`Avizoro.Core`, `Avizoro.Maui`,
  `Avizoro.Hosting`, `Avizoro.Extensions.Logging`, `Avizoro.AspNetCore`, `Avizoro.Blazor`, `Avizoro.Wpf`,
  `Avizoro.WinForms`, `Avizoro.Avalonia`). The old packages get no further releases.
- **API names**: namespaces `IntelligenceKit.*` are `Avizoro.*`; `UseIntelligenceKit` / `AddIntelligenceKit`
  are `UseAvizoro` / `AddAvizoro`; `IIntelligenceKit` is `IAvizoro`; `IntelligenceKitSdk` is `AvizoroSdk`;
  `IntelligenceEvent`, `IntelligenceOptions`, `IntelligenceScope` and `IntelligenceDsn` are `AvizoroEvent`,
  `AvizoroOptions`, `AvizoroScope` and `AvizoroDsn`. Members and behavior are unchanged, so upgrading is
  a find-and-replace. The logging provider alias is `Avizoro` (`Logging:Avizoro` in configuration).
- **Symbol upload (MSBuild)**: the `IntelligenceKit*` properties are `Avizoro*`, and `IK_ADMIN_TOKEN` is
  `AVIZORO_ADMIN_TOKEN`.
- **Server metrics**: the meter is `Avizoro.Server` and the instruments are `avizoro.*` (were `ik.*`), so
  Prometheus names change from `ik_…` to `avizoro_…`. Update dashboards and alerts that use them.
- **Docker**: images are `avizoro-server` and `avizoro-dashboard`. In `docker-compose.yml` the variables
  are `AVIZORO_READ_TOKEN` and `AVIZORO_VERSION` (were `IK_…`), and the Compose project name and the
  default Postgres user/database are `avizoro`. To keep an existing Compose volume, set
  `COMPOSE_PROJECT_NAME=intelligencekit` and keep your `POSTGRES_*` values in `.env`.
- New project and read keys start with `avzp_` and `avzr_`; existing `ikp_` and `ikr_` keys keep working.
- The dashboard stores its token and theme under new browser keys, so it asks for the token once more.

### Compatibility with 1.x

- The server accepts the ingest header `X-IntelligenceKit-Key` as well as `X-Avizoro-Key`.
- Signed alert webhooks carry both `X-Avizoro-Signature` and `X-IntelligenceKit-Signature`.
- A SQLite server configured with the default `avizoro.db` keeps using an existing `intelligencekit.db`
  in the same folder.
- An app upgraded from a 1.x SDK keeps its installation id and its unsent events (MAUI and the
  hosting/desktop SDKs move their offline queue on first start).

### Added

- **Session timeline** (`EnableSessionTimeline`, off by default): the SDK records the whole session
  (screens, user actions, HTTP calls, logs, screenshots), not only the breadcrumbs attached to errors,
  and sends it in batches through the offline queue, so the steps before a crash survive it. Errors and
  logs carry their `SessionId` and appear in place. Volume controls: `SessionTimelineMaxEntries`,
  `SessionTimelineSampleRate`, `SessionTimelineFlushInterval`. MAUI and the desktop/hosting SDKs.
- **Sessions page** in the dashboard, and a **session detail** page with the timeline: category
  filters, search, expandable steps, screenshots, links to error events, and live updates while the
  session runs. Events link to their session.
- **On-demand screenshots** on MAUI: inject `IScreenCapture` and call `CaptureToTimelineAsync()`.
  `ScreenCaptureExcludedPages` is respected.
- `kit.TrackAction(...)` and `kit.TrackScreen(...)` extension methods for readable user-action and
  screen breadcrumbs.
- HTTP breadcrumbs include `request_size` and `response_size` (from `Content-Length`; bodies are never read).
- Server: `GET /sessions`, `GET /sessions/{id}`, `GET /sessions/{id}/timeline`; SignalR messages
  `sessionUpdated` and `timelineReceived`; sessions store device model, manufacturer and OS; the
  `avizoro.timeline.entries.ingested` metric; retention also prunes timeline entries. Migration
  `SessionTimeline` for SQLite, PostgreSQL and SQL Server.
- Demo data includes recent sessions with a timeline, one of them crashing on "Pay".

## [1.1.0] - 2026-09-24

The SDK API is extended, not changed. Apps built against 1.0 compile and run unchanged.

> **Upgrade the server first.** A 1.1 SDK also sends session, performance-span and
> feedback events. A 1.0 server stores them as ordinary events, so they clutter the
> Events and Issues pages and give no crash-free rates, spans or feedback. A 1.1 server routes
> them properly. It migrates its database on startup.

### Added

- **Issue lifecycle**: resolve (optionally *in a release*), ignore, reopen and assign
  issues (`PATCH /issues/{id}`). A resolved issue that gets a new event is reopened
  and flagged as a **regression**, unless the event comes from an older release than
  the fix. Issues record the release that introduced them and the latest release seen.
- **Alerts**: rules for new issues, regressions and event-count thresholds, sent to
  a signed webhook, Slack, Teams, Discord or email (SMTP), with a per-issue cooldown,
  delivery history and a test button.
- **Sessions and crash-free rates**: automatic session tracking in every SDK;
  crash-free sessions and users on the Overview (`GET /stats/crash-free`).
- **Release health**: a Releases page with adoption, crash-free rates, session and
  exception counts, and the new issues each release introduced (`GET /releases`).
- **ANR detection**: a UI-thread watchdog in MAUI, WPF, WinForms and Avalonia reports
  `ApplicationNotResponding` when the UI is frozen longer than `AnrThreshold`.
- **Symbolication**: upload portable PDBs and Android R8/ProGuard mappings
  (`POST /symbols`, or automatically after a Release build with
  `IntelligenceKitUploadSymbols=true`). Release stack traces get file and line
  numbers, and Java frames are deobfuscated.
- **Privacy and volume controls**: `BeforeSend`, `BeforeBreadcrumb`, `SampleRate`,
  and PII scrubbing, which is on by default (`EnablePiiScrubbing`,
  `ScrubbingSensitiveKeys`, `ScrubbingPatterns`).
- **Performance monitoring**: app start, page load, HTTP client
  (`AddIntelligenceKitHandler()`) and ASP.NET Core request spans, plus
  `IPerformanceMonitor.StartSpan` for your own. A Performance page shows p50, p75,
  p95 and failure rate.
- **Better grouping**: issues are grouped by the top *in-app* frame instead of
  framework frames. Log events are grouped by message template. Custom fingerprints
  are supported (`IntelligenceEvent.Fingerprint`, with `{{ default }}`), and
  `POST /admin/issues/backfill` regroups stored events.
- **New packages**: `IntelligenceKit.Extensions.Logging` (ILogger provider),
  `IntelligenceKit.Hosting` (console apps, workers, services),
  `IntelligenceKit.AspNetCore`, `IntelligenceKit.Blazor`, `IntelligenceKit.Wpf`,
  `IntelligenceKit.WinForms` and `IntelligenceKit.Avalonia`.
- **MAUI on Windows and Mac Catalyst**, alongside Android and iOS.
- **User feedback**: `CaptureFeedbackAsync` and `LastEventId`, and an opt-in prompt
  after a crash in MAUI (`EnableCrashFeedbackPrompt`). Feedback appears on events and
  issues.
- **Search and filters**: status tabs and text search on Issues; text, level,
  environment, release, platform, OS, device, user, tag and date filters on Events.
- **Issue insights**: distributions by platform, OS, device, manufacturer, release,
  environment and tags; affected users; and a per-user timeline page.
- **Projects page** in the dashboard: create projects, copy the DSN, rotate read
  keys and delete projects.
- **Export and issue trackers**: CSV/JSON export of events and issues; create a
  GitHub issue (prefilled form or API) or a Jira issue from an issue.
- **Server observability**: `/health/live` and `/health/ready` probes, Prometheus
  `/metrics`, and optional OTLP export.
- **Deployment**: server and dashboard images on GitHub Container Registry (amd64 and
  arm64), `$PORT` support, and one-click templates for Render, Azure App Service and
  Railway. The server also accepts `postgres://` URLs and `DATABASE_URL`.
- **Demo mode**: `Demo:Seed` fills an empty database with sample data, and
  `Demo:ReadOnly` rejects all writes (`docker-compose.demo.yml`).
- **Documentation site** built from `docs/` and published to GitHub Pages.

### Changed

- `IIntelligenceKit` gained `CaptureFeedbackAsync` and `LastEventId` as default
  interface members, so existing implementations still compile.
- `HttpIntelligenceClient` moved to `IntelligenceKit.Core`, so every SDK can use it.
  The MAUI type of the same name remains for compatibility.

## [1.0.0] - 2026-08-19

First stable release. The API is now frozen under SemVer.

### Added

- **MAUI SDK** — automatic crash reporting on Android & iOS (typed, nested
  `ExceptionInfo`), offline store-and-forward queue, rich context (breadcrumbs,
  device runtime snapshot, tags/user/environment/release), opt-in last-screen
  capture, and `TrackLogAsync`. One-line setup: `builder.UseIntelligenceKit(dsn)`.
- **Backend** — minimal-API ingest/query server on EF Core with **SQLite,
  PostgreSQL and SQL Server** providers; idempotent ingest; **issue grouping** by
  fingerprint; real-time **SignalR** push; errors-per-hour and per-project stats.
- **Per-project scoping (multi-tenant)** — a `Project` registry with a per-project
  read key that sees only its own data, plus an admin-only management API
  (`/admin/projects` create/list/rotate-key/delete). The global admin token still
  sees everything. Read endpoints and the SignalR hub filter by the caller's scope.
- **Ingest hardening** — per-client-IP rate limiting (`429` + `Retry-After`, config
  under `RateLimit:Ingest`) and known-project validation (config
  `Ingest:RequireKnownProject`).
- **Data retention** — an opt-in background sweep prunes events/screenshots and
  stale issues older than `Retention:Days`.
- **Dashboard** — Blazor WebAssembly live feed + charts.
- **Ops** — one-command Docker Compose stack (PostgreSQL + server + dashboard),
  GitHub Actions CI, and a 102-test suite (Core unit + Server integration).

### Changed

- **BREAKING (since `0.1.0-alpha`): ingest now validates the project by default.**
  `POST /events` only accepts events whose `(projectId, projectKey)` pair matches a
  project registered via `POST /admin/projects`; unknown projects get `404`. Set
  `Ingest:RequireKnownProject: false` to keep ingest fully open as before.
- **Read-side auth** is no longer a single shared token only: a presented credential
  is resolved to either the admin token (sees all) or a per-project read key (scoped).
  The existing admin-token flow is unchanged.
- The SDK now treats HTTP `429`/`408` as transient (store-and-forward retries them)
  instead of dropping the event, so ingest rate limiting never loses data.

### Security

- Read API, SignalR hub and dashboard remain gated (admin token or project read key);
  open in Development when unset, fail-closed in Production.
- Project read keys are stored only as a SHA-256 hash and shown once at
  creation/rotation.

## [0.1.0-alpha.1] - 2026-08-18

- Initial alpha packages (`IntelligenceKit.Core`, `IntelligenceKit.Maui`) published to
  NuGet. End-to-end SDK → server → dashboard flow with crash reporting, offline
  store-and-forward, rich context, last-screen capture, multi-provider backend,
  real-time dashboard, read-side auth and issue grouping.

[2.0.0]: https://github.com/avizoro/avizoro/releases/tag/v2.0.0
[1.1.0]: https://github.com/wilsonvargas/IntelligenceKit/releases/tag/v1.1.0
[1.0.0]: https://github.com/wilsonvargas/IntelligenceKit/releases/tag/v1.0.0
[0.1.0-alpha.1]: https://github.com/wilsonvargas/IntelligenceKit/releases/tag/v0.1.0-alpha.1
