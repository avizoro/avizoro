# Deploying Avizoro

Every option runs the same two images, which are built by
[`.github/workflows/docker.yml`](../.github/workflows/docker.yml) and published to
GitHub Container Registry:

| Image | Port | Key settings |
|---|---|---|
| `ghcr.io/avizoro/avizoro-server` | `$PORT` (default 7099) | `Auth__ReadToken`, `Database__Provider`, `ConnectionStrings__Events` (or `DATABASE_URL`) |
| `ghcr.io/avizoro/avizoro-dashboard` | `$PORT` (default 80) | `API_BASE_URL`, the server URL **as the browser reaches it** |

Tags: `latest` (default branch), `1.2.3` / `1.2` (releases), `sha-<commit>`.

> **First publish:** GHCR creates packages as *private*. Open each package under
> GitHub → Packages → *Package settings* and set its visibility to **Public** so
> Docker, Azure, Render and Railway can pull it without credentials.

## Docker Compose (any VM)

```bash
cp .env.example .env         # set AVIZORO_READ_TOKEN (and API_BASE_URL for a remote host)
docker compose pull && docker compose up -d
```

This runs PostgreSQL, the server and the dashboard. Use `docker compose up --build`
to build from source instead.

## Render: one click, with managed PostgreSQL

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/avizoro/avizoro)

The [`render.yaml`](../render.yaml) Blueprint creates `avizoro-db` (PostgreSQL),
`avizoro-server` and `avizoro-dashboard`. Render asks for two values:

- `API_BASE_URL` (dashboard): the public URL of `avizoro-server`, for example
  `https://avizoro-server.onrender.com`.
- `Alerts__DashboardUrl` (server): the public URL of `avizoro-dashboard`. It is used for
  links in alerts and in GitHub/Jira issues.

Service URLs are `https://<name>.onrender.com`, unless Render adds a suffix. You
can fix either value later in the service's *Environment* tab. The admin token is
generated for you: copy `Auth__ReadToken` from `avizoro-server` → *Environment*.

## Azure App Service

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Favizoro%2Favizoro%2Fmaster%2Fdeploy%2Fazure%2Fazuredeploy.json)

[`azure/azuredeploy.json`](azure/azuredeploy.json) creates a Linux App Service
plan with two container apps: `<prefix>-server` and `<prefix>-dashboard`. The
server uses SQLite on the persistent `/home` share, and both apps are already
wired to each other. You only need to choose the admin token. The deployment
outputs include the dashboard URL and a DSN template.

To use PostgreSQL or SQL Server instead of SQLite, change `Database__Provider` and
`ConnectionStrings__Events` in the server's *Configuration*.

## Railway

1. Create a project, then add a **PostgreSQL** database.
2. Add a service from this repository. Under *Settings → Config-as-code*, point it
   at `deploy/railway/server.railway.json`. Then set these variables:
   - `Database__Provider=PostgreSql`
   - `DATABASE_URL=${{Postgres.DATABASE_URL}}` (the server accepts `postgres://` URLs)
   - `Auth__ReadToken=<long random string>`
3. Add a second service from the same repository, with
   `deploy/railway/dashboard.railway.json`. Set
   `API_BASE_URL=https://${{server.RAILWAY_PUBLIC_DOMAIN}}`, using the server service's name.
4. Generate a public domain for both services.

Both images listen on Railway's `$PORT`.

## After deploying

- Open the dashboard, enter the admin token, and create a project under
  **Projects**. It shows the DSN to put in your app.
- Health probes: `GET /health/live` and `/health/ready`.
- Metrics: `GET /metrics` (Prometheus, admin token), or set
  `Telemetry__Otlp__Endpoint` to push metrics and traces to a collector.
