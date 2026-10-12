---
title: Server configuration
nav_order: 5
---

# Server configuration
{: .no_toc }

The server is configured through `appsettings.json` or environment variables. In an environment variable, `:` becomes `__`, so `Auth:ReadToken` is `Auth__ReadToken`.

1. TOC
{:toc}

## Database

```jsonc
"Database": { "Provider": "Sqlite" },     // Sqlite | PostgreSql | SqlServer
"ConnectionStrings": { "Events": "Data Source=avizoro.db" }
```

- SQLite is the default and needs no setup. The folder of the database file is created if it's missing.
- PostgreSQL and SQL Server need `ConnectionStrings:Events`. For PostgreSQL, a `postgres://user:pass@host:5432/db` URL, as handed out by Render, Railway or Heroku, is also accepted, and so is `DATABASE_URL`.
- The schema is migrated on startup.

## Authentication

```jsonc
"Auth": { "ReadToken": "avizoro_admin_<long random string>" }
```

- The **admin token** can read everything and manage projects, alerts and symbols. Send it as `Authorization: Bearer <token>`.
- Each project also has a **read key** (created or rotated on the Projects page) that only sees that project.
- With no token set, reads are open in `Development` and blocked in `Production`.
- **Ingest** (`POST /events`) is open by design: the project key in the DSN is public. By default only registered projects are accepted:

```jsonc
"Ingest": { "RequireKnownProject": true },
"RateLimit": { "Ingest": { "Enabled": true, "PermitLimit": 300, "WindowSeconds": 60 } }
```

Rate limiting is per client IP. A throttled SDK gets `429` and retries later, so no events are lost.

{: .warning }
Run the server behind HTTPS in production.

## Retention

Off by default, so a new install never deletes anything:

```jsonc
"Retention": { "Enabled": true, "Days": 90, "SweepHours": 6 }
```

## Alerts

```jsonc
"Alerts": {
  "DashboardUrl": "https://avizoro.example.com",          // for "open issue" links
  "Smtp": { "Host": "smtp.example.com", "Port": 587, "EnableSsl": true,
            "User": "", "Password": "", "From": "alerts@example.com" }
}
```

Rules are created in the dashboard. SMTP is only needed for email rules. See [Alerts](features.md#alerts).

## Issue trackers

```jsonc
"Integrations": {
  "GitHub": { "Repository": "owner/repo", "Token": "" },
  "Jira": { "BaseUrl": "https://you.atlassian.net", "Email": "", "ApiToken": "",
            "ProjectKey": "APP", "IssueType": "Bug" }
}
```

- **GitHub** with only `Repository` set: the dashboard opens a prefilled new-issue form. Add a fine-grained token with *Issues: write* to create the issue directly.
- **Jira Cloud** needs all five values.

## Observability of the server itself

```jsonc
"Telemetry": {
  "Prometheus": { "Enabled": true, "RequireAuth": true },
  "Otlp": { "Endpoint": "http://otel-collector:4317" }
}
```

- `GET /health/live`: the process is up.
- `GET /health/ready`: the database can be reached. Use this for load balancer and orchestrator probes.
- `GET /metrics`: Prometheus metrics: events ingested and rejected, ingest duration, issue changes, session updates, spans, and alerts delivered (`avizoro.*`), plus ASP.NET Core and .NET runtime metrics. It needs the admin token unless `RequireAuth` is `false`.
- With `Otlp:Endpoint` set, metrics and traces are also pushed over OTLP.

## Demo mode

```jsonc
"Demo": { "Seed": true, "ReadOnly": true }
```

- `Seed` fills an **empty** database with two weeks of sample data for a project called `demo-shop`.
- `ReadOnly` rejects every write, including ingest, with `403`, so the instance can be shared publicly with a known token.

## Dashboard

The dashboard is a static Blazor WebAssembly app. It needs to know where the API is:

- From source: `wwwroot/appsettings.json` → `ApiBaseUrl`.
- Docker image: the `API_BASE_URL` environment variable, the server URL **as the browser reaches it**.

The server allows any origin in CORS by default, so the dashboard and Blazor apps can be served from another host.
