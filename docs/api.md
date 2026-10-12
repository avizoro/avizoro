---
title: HTTP API
nav_order: 6
---

# HTTP API
{: .no_toc }

The dashboard uses this API, and you can too, for scripts, reports and integrations. Enums are strings in JSON (`"Exception"`), and numbers are also accepted on input. Lists are paged with `skip` and `take` and return `{ total, skip, take, items }`.

**Auth** column:
- *open*: no token needed.
- *read*: the admin token or a project read key. A project key only sees its own project.
- *admin*: the admin token only.

1. TOC
{:toc}

## Ingest

| Method | Path | Auth | |
|---|---|---|---|
| `POST` | `/events` | open | Ingest one event. Idempotent by event `id`. Sessions, session timeline batches, performance spans and feedback use the same endpoint with their own `eventType`. |
| `POST` | `/events/{id}/screenshot` | open | Screenshot for an event (JPEG, up to 2 MB). |

## Issues

| Method | Path | Auth | |
|---|---|---|---|
| `GET` | `/issues` | read | `projectId`, `status` (`Unresolved`/`Resolved`/`Ignored`), `q`, `release`, `level`, `eventType`, `assignedTo`, `skip`, `take` |
| `GET` | `/issues/{id}` | read | |
| `PATCH` | `/issues/{id}` | read | `{ "status": "Resolved", "resolvedInRelease": "2.4.1", "assignedTo": "ana" }`. Any subset of these fields. |
| `GET` | `/issues/{id}/events` | read | Latest occurrences. |
| `GET` | `/issues/{id}/distributions` | read | Breakdown by platform, OS, device, release, environment and tags. |
| `GET` | `/issues/{id}/users` | read | Affected users with event counts. |
| `GET` | `/issues/{id}/feedback` | read | |
| `POST` | `/issues/{id}/external/{provider}` | read | `github` or `jira`. Creates (or drafts) a tracker issue. |
| `GET` | `/issues/export` | read | `projectId`, `status`, `format` (`csv` or `json`) |
| `POST` | `/admin/issues/backfill` | admin | Regroup stored events into issues. |

## Events

| Method | Path | Auth | |
|---|---|---|---|
| `GET` | `/events` | read | `projectId`, `eventType`, `q`, `level`, `release`, `environment`, `platform`, `operatingSystem`, `deviceModel`, `userId`, `tag=key:value` (repeatable), `from`, `to`, `skip`, `take` |
| `GET` | `/events/{id}` | read | Full detail: exception tree, breadcrumbs, device state, tags. |
| `GET` | `/events/{id}/screenshot` | read | Also accepts `?access_token=` for `<img>` tags. |
| `GET` | `/events/{id}/feedback` | read | |
| `GET` | `/events/export` | read | The `/events` filters plus `format` (`csv` or `json`). |

## Health, releases and performance

| Method | Path | Auth | |
|---|---|---|---|
| `GET` | `/stats/crash-free` | read | `projectId`, `environment`, `release`, `days` (default 14) |
| `GET` | `/stats/events-per-hour` | read | |
| `GET` | `/releases` | read | `projectId`, `environment`, `days` (default 30) |
| `GET` | `/performance` | read | `projectId`, `operation`, `release`, `environment`, `days` (default 7) |

## Sessions

| Method | Path | Auth | |
|---|---|---|---|
| `GET` | `/sessions` | read | `projectId`, `environment`, `release`, `status` (`Ok`, `Exited`, `Crashed`), `userId`, `withTimeline`, `skip`, `take`. Newest first. |
| `GET` | `/sessions/{id}` | read | One session. |
| `GET` | `/sessions/{id}/timeline` | read | The session's recorded steps and its errors, in order (at most 5000 steps). Screenshot steps carry a `screenshotId` for `GET /events/{id}/screenshot`. |

## Users and feedback

| Method | Path | Auth | |
|---|---|---|---|
| `GET` | `/users/{userId}` | read | `projectId`, `take`. Timeline, issues and session counts for one user. |
| `GET` | `/feedback` | read | `projectId`, `skip`, `take` |

## Projects

| Method | Path | Auth | |
|---|---|---|---|
| `GET` | `/projects` | read | Projects that have sent events. |
| `GET` | `/admin/projects` | admin | Registered projects. |
| `POST` | `/admin/projects` | admin | `{ "projectId", "name", "projectKey"? }`. Returns the read key **once**. |
| `POST` | `/admin/projects/{id}/rotate-key` | admin | New read key. |
| `DELETE` | `/admin/projects/{id}` | admin | Unregister. Stored events are kept. |

## Alerts

| Method | Path | Auth | |
|---|---|---|---|
| `GET` | `/alerts/rules` | admin | `projectId` |
| `POST` | `/alerts/rules` | admin | `{ "name", "trigger", "channel", "target", "projectId"?, "thresholdCount"?, "thresholdWindowMinutes"?, "secret"?, "enabled"?, "cooldownMinutes"? }` |
| `PUT` | `/alerts/rules/{id}` | admin | Same body. |
| `DELETE` | `/alerts/rules/{id}` | admin | |
| `POST` | `/alerts/rules/{id}/test` | admin | Send a test notification. |
| `GET` | `/alerts/history` | admin | `projectId`, `issueId`, `skip`, `take` |

## Symbols

| Method | Path | Auth | |
|---|---|---|---|
| `POST` | `/symbols` | admin | Multipart. Portable PDBs, assemblies with embedded PDBs, or an Android `mapping.txt` (which also needs the `projectId` and `release` form fields). |
| `GET` | `/symbols` | admin | Uploaded files. |
| `DELETE` | `/symbols/{id}` | admin | |

## Integrations and operations

| Method | Path | Auth | |
|---|---|---|---|
| `GET` | `/integrations` | read | Which issue trackers are configured. |
| `GET` | `/health/live` | open | |
| `GET` | `/health/ready` | open | Checks the database. |
| `GET` | `/metrics` | admin | Prometheus. Open if `Telemetry:Prometheus:RequireAuth` is `false`. |

## Live updates

The SignalR hub at `/hubs/events` (read auth, `?access_token=` for WebSockets) pushes `eventReceived`, `issueUpserted`, `sessionUpdated` (a session started or changed) and `timelineReceived` (new timeline steps of a session) messages.

## Example

```bash
# Unresolved issues in 2.4.0, newest first
curl -H "Authorization: Bearer $IK_TOKEN" \
  "https://ik.example.com/issues?projectId=my-app&status=Unresolved&release=2.4.0"

# Resolve one in the next release
curl -X PATCH -H "Authorization: Bearer $IK_TOKEN" -H "Content-Type: application/json" \
  -d '{ "status": "Resolved", "resolvedInRelease": "2.4.1" }' \
  "https://ik.example.com/issues/$ISSUE_ID"
```
