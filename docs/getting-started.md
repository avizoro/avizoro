---
title: Getting started
nav_order: 2
---

# Getting started
{: .no_toc }

1. TOC
{:toc}

## Try the demo

The quickest way to see Avizoro is the demo stack. It seeds two weeks of sample data (a shop app with three releases, a regression, an ANR, slow endpoints and user feedback) and runs read-only:

```bash
git clone https://github.com/avizoro/avizoro
cd Avizoro
AVIZORO_READ_TOKEN=demo docker compose -f docker-compose.yml -f docker-compose.demo.yml up -d
```

Open **http://localhost:8080** and sign in with the token `demo`.

Without Docker, run the server with the same settings from source:

```bash
Demo__Seed=true Auth__ReadToken=demo dotnet run --project src/Avizoro.Server
dotnet run --project src/Avizoro.Dashboard      # http://localhost:5292
```

Seeding only runs on an **empty** database, so it never touches real data.

## 1. Run the server

With Docker (PostgreSQL + server + dashboard):

```bash
cp .env.example .env        # set AVIZORO_READ_TOKEN to a long random value
docker compose up -d
```

- Dashboard: **http://localhost:8080**
- API: **http://localhost:7099**

From source, with SQLite and no setup:

```bash
dotnet run --project src/Avizoro.Server        # http://0.0.0.0:7099
dotnet run --project src/Avizoro.Dashboard     # http://localhost:5292
```

The server creates and migrates its database on startup. To run on a cloud host, see [Deploying](https://github.com/avizoro/avizoro/blob/master/deploy/README.md).

## 2. Create a project

Open the dashboard, enter the admin token (`Auth:ReadToken`), and go to **Projects → New project**. The page shows the project's **DSN**:

```
https://{projectKey}@{host}/{projectId}
```

The project key is a public routing id that ships inside your app. It is not a secret. By default the server only accepts events for registered projects (`Ingest:RequireKnownProject`).

You can also create a project from a script:

```bash
curl -X POST http://localhost:7099/admin/projects \
  -H "Authorization: Bearer $ADMIN_TOKEN" -H "Content-Type: application/json" \
  -d '{ "projectId": "my-app", "name": "My App" }'
```

## 3. Add the SDK

For a MAUI app:

```bash
dotnet add package Avizoro.Maui
```

```csharp
// MauiProgram.cs
builder
    .UseMauiApp<App>()
    .UseAvizoro("http://projectKey@10.0.2.2:7099/my-app", o =>
    {
        o.Environment = "staging";
    });
```

{: .note }
The Android emulator reaches your machine at `10.0.2.2`, not `localhost`.

That one call sets up crash capture, the offline queue, sessions, ANR detection, navigation breadcrumbs, page-load timing and `ILogger` integration. App name and version are detected automatically. For other app types, see [SDKs](sdks.md).

## 4. Send an event

Crashes are captured automatically. To report something yourself, inject `IAvizoro`:

```csharp
public class CheckoutViewModel(IAvizoro kit)
{
    async Task PayAsync()
    {
        kit.SetUser("user-42");                  // optional, an anonymous id
        kit.SetTag("plan", "pro");
        kit.AddBreadcrumb("Tapped Pay");
        try { /* ... */ }
        catch (Exception ex)
        {
            await kit.TrackExceptionAsync(ex);
        }
    }
}
```

The event appears in the dashboard's **Events** feed right away, and in **Issues**, grouped with every other event that has the same exception type and top in-app frame.

## Next steps

- Upload symbols so release-build stack traces show [file and line numbers](features.md#readable-stack-traces).
- Set up [alerts](features.md#alerts) for new issues and regressions.
- Tag builds with a `Release` to get [release health](features.md#sessions-and-release-health).
