---
title: Features
nav_order: 4
---

# Features
{: .no_toc }

1. TOC
{:toc}

## Issues and regressions

Events with the same exception type and the same top **in-app** stack frame are grouped into one issue. Framework frames such as `System.*` and `Microsoft.*` are skipped, so a `NullReferenceException` inside LINQ is grouped by your code that called LINQ. Log events are grouped by their message template.

Each issue has a status:

| Status | Meaning |
|---|---|
| **Unresolved** | New or still happening. |
| **Resolved** | Fixed, optionally *in a release* (for example `2.4.1`). |
| **Ignored** | Muted. New events are still stored. |

A new event on a **resolved** issue reopens it and flags it as a **regression**. If the issue was resolved in a release, events from strictly older releases don't reopen it, because those users simply haven't updated yet. Issues also record the release that introduced them and the latest release they were seen in, and can be assigned to a person.

{% raw %}
To group events your own way, set a fingerprint on the event. `{{ default }}` stands for the normal grouping:

```csharp
await kit.TrackAsync(new IntelligenceEvent
{
    EventType = EventType.Exception,
    Exception = ExceptionInfo.FromException(ex),
    Fingerprint = ["{{ default }}", tenantId],   // one issue per tenant
});
```
{% endraw %}

Grouping happens when an event arrives. After a server upgrade that changes grouping, regroup the stored events with `POST /admin/issues/backfill`.

## Sessions and release health

The SDKs track **sessions** automatically: one per foreground period in MAUI, one per run on the desktop, one per process in hosted apps. A session ends as `Exited` (normal), `Crashed` (fatal crash) or `Abnormal` (it stopped reporting while still in the foreground).

From sessions the server computes:

- **Crash-free sessions** and **crash-free users**, shown on the Overview.
- **Release health** on the Releases page: adoption over the last 24 hours, crash-free rates, session and exception counts, and the new issues each release introduced. Releases below 99% crash-free sessions are highlighted.

Set `Release` in the options, or let the SDK use the app version.

The **Sessions** page lists every session with its device, OS, release, duration and errors, and updates live.

## Session timeline

Breadcrumbs normally ride along with an error, so a session without errors leaves no trace. With
`EnableSessionTimeline = true` the SDK also records the **whole session** and the dashboard shows it as a
timeline: every screen, user action, HTTP call (method, host/path, status, duration, sizes), log line and
screenshot, with the session's errors and crashes in place. Open a running session and new steps appear
as they happen, without refreshing. From an event, **Open session timeline** shows what led up to it.

- Recording rides the same offline queue as events, so the steps before a crash survive it and are
  delivered on the next launch. Entries are sent in batches (every 5 s, every 50 entries and when the
  app goes to the background).
- Everything that enters the breadcrumb trail is recorded, after `BeforeBreadcrumb` and PII scrubbing.
  Record your own steps with `kit.TrackAction("pay_clicked")` and `kit.TrackScreen("SettingsTab")`.
- On MAUI, inject `IScreenCapture` and call `CaptureToTimelineAsync()` to add a screenshot of the
  current screen. Pages in `ScreenCaptureExcludedPages` are never captured.
- Volume controls: `SessionTimelineMaxEntries` (1000 per session), `SessionTimelineSampleRate`
  (fraction of sessions recorded) and `SessionTimelineFlushInterval`. The server keeps at most 200
  entries per batch and 5000 per session, and retention prunes old entries with the rest of the data.

It is **off by default**: it sends more data, and a timeline describes what a user did.

## ANR detection

A watchdog pings the UI thread. If it doesn't answer within `AnrThreshold` (5 s by default), an `ApplicationNotResponding` event is sent, with the UI thread's stack where the platform allows it. One long freeze is one report. If the whole app was suspended (for example in the background), that round is discarded instead of being reported. This is available in MAUI, WPF, WinForms and Avalonia.

## Performance

| Operation | Recorded by |
|---|---|
| `app.start` | MAUI: process start to the first page on screen (cold start). |
| `ui.load` | MAUI: each page, from navigation to appearing. |
| `http.client` | `AddIntelligenceKitHandler()` on an `HttpClient`. |
| `http.server` | ASP.NET Core: each request, by route template. |
| your own | `IPerformanceMonitor.StartSpan(operation, name)` |

Spans are batched and sent every `PerformanceFlushInterval` (60 s by default), when the app goes to the background, or every 50 spans. URLs are normalized (`/orders/123` becomes `/orders/{id}`) and query strings are dropped. The **Performance** page shows count, p50, p75, p95 and failure rate per operation, filterable by project, release and time window.

## Readable stack traces

Release builds lose file and line numbers, and Android R8 obfuscates Java frames. Upload the symbols and the server fills them in when an event arrives.

Add this to the app's project file:

```xml
<PropertyGroup Condition="'$(Configuration)' == 'Release'">
  <IntelligenceKitUploadSymbols>true</IntelligenceKitUploadSymbols>
  <IntelligenceKitServerUrl>https://ik.example.com</IntelligenceKitServerUrl>
  <IntelligenceKitProjectId>my-app</IntelligenceKitProjectId>
</PropertyGroup>
```

Provide the admin token as the `IK_ADMIN_TOKEN` environment variable (or `IntelligenceKitAdminToken`), not in source control. After the build, the target uploads the assemblies, portable PDBs and the Android `mapping.txt`. It never fails the build: problems are reported as warnings.

- .NET frames are matched by the assembly's **MVID**, so they always resolve against the exact build.
- ProGuard/R8 mappings are matched by project and release.
- Symbolicated events show a *symbolicated* badge in the dashboard.

You can also upload files by hand: `POST /symbols` (multipart, admin token).

## Alerts

Go to **Alerts** in the dashboard, or use `/alerts/rules`, to create rules:

| Trigger | Fires when |
|---|---|
| `NewIssue` | An issue is seen for the first time. |
| `Regression` | A resolved issue comes back. |
| `Threshold` | An issue receives *N* events within *M* minutes. |

Channels are `Webhook`, `Slack`, `Teams`, `Discord` and `Email` (SMTP under `Alerts:Smtp`). A rule can target one project or all of them. `CooldownMinutes` stops the same rule from firing again for the same issue too soon. Deliveries run in the background, and each one (sent or failed, with the error) is listed under **Recent deliveries**. The **Test** button checks a channel without waiting for a real issue.

Generic webhooks receive JSON. When the rule has a secret, the body is signed:

```
X-IntelligenceKit-Signature: sha256=<hex HMAC-SHA256 of the raw body>
```

Set `Alerts:DashboardUrl` so alerts link to the issue.

## User feedback

- **After a crash** (MAUI): with `EnableCrashFeedbackPrompt = true`, the next launch asks the user what they were doing, and the answer is attached to the crash.
- **Anywhere**: attach comments to the last event:

```csharp
await kit.CaptureFeedbackAsync(new UserFeedback
{
    EventId = kit.LastEventId!.Value,
    Comments = "It crashed when I tapped Pay",
    Email = "optional@example.com",
});
```

Feedback is shown on the event, on its issue, and through `GET /feedback`.

## Investigating an issue

The issue page shows:

- **Distributions** over the latest events: platform, OS, device, manufacturer, release, environment and the most common tags.
- **Affected users**, each linking to a **user timeline** with everything that user ran into, their issues and their sessions.
- Feedback, the latest occurrences, and triage actions (resolve, ignore, reopen, assign).
- **Create a GitHub or Jira issue** with the stack trace and a link back. With only `Integrations:GitHub:Repository` set, a prefilled GitHub form opens. Add a token to create the issue through the API. The link is saved on the issue.

## Search, filters and export

- **Issues**: status tabs, a text search over title and culprit, and a project filter. The API also filters by release, level, event type and assignee (`GET /issues?release=2.4.0&assignedTo=ana`).
- **Events**: text search, and filters for type, level, environment, release, platform, OS, device model, user, tag (`key:value`) and date range.
- **Export**: `GET /events/export` takes the same filters as the Events page, and `GET /issues/export` takes `projectId` and `status`. Both return CSV, or JSON with `?format=json`. CSV cells are protected against spreadsheet formula injection.

## Privacy

- **PII scrubbing** is on by default. Emails, bearer tokens, JWTs and card numbers are masked in messages, breadcrumbs and data. Values of keys such as `password`, `token` and `secret` become `[Filtered]`. Add your own with `ScrubbingSensitiveKeys` and `ScrubbingPatterns`.
- **`BeforeSend`** can change or drop any event, and **`BeforeBreadcrumb`** any breadcrumb.
- **`SampleRate`** keeps a fraction of non-fatal events. Crashes are always kept.
- **User ids** are opt-in (`SetUser`) and should be anonymous.
- **Screen capture** is off by default, can skip pages, and is downscaled.
