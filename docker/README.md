# Running Avizoro with Docker

A one-command self-hosted stack: **PostgreSQL + ingest/query server + dashboard**.

## Quick start

```bash
cp .env.example .env
# edit .env and set AVIZORO_READ_TOKEN to a long random value, e.g. `openssl rand -hex 24`

docker compose up --build
```

Then open:

| Service   | URL                                     |
| --------- | --------------------------------------- |
| Dashboard | http://localhost:8080                   |
| API       | http://localhost:7099                   |

The dashboard prompts once for the read token (`AVIZORO_READ_TOKEN`) and remembers it
in the browser. Point a MAUI app at the API with a DSN like
`http://demo-key@localhost:7099/my-project` (use your host's LAN IP or the
`10.0.2.2` alias from an Android emulator).

## What's in the box

- **db** — `postgres:16-alpine`, data persisted in the `pgdata` volume, with a
  `pg_isready` healthcheck the server waits on.
- **server** — multi-stage .NET 10 build of `Avizoro.Server`, running as
  `Production` on port `7099`. It is wired to Postgres via config env vars and
  **applies EF migrations on startup**, so the schema is created automatically.
- **dashboard** — the Blazor WebAssembly app published to static files and served
  by nginx (SPA fallback + WASM MIME/caching).

## Configuration

Everything is driven by `.env` (see `.env.example`):

| Variable            | Default                  | Purpose                                                            |
| ------------------- | ------------------------ | ------------------------------------------------------------------ |
| `AVIZORO_READ_TOKEN`     | — (**required**)         | Gates the read API + dashboard. Production fails closed without it. |
| `POSTGRES_USER`     | `avizoro`        | Postgres role (shared by `db` and `server`).                       |
| `POSTGRES_PASSWORD` | `avizoro`        | Postgres password.                                                 |
| `POSTGRES_DB`       | `avizoro`        | Database name.                                                     |
| `SERVER_PORT`       | `7099`                   | Host port mapped to the API.                                       |
| `DASHBOARD_PORT`    | `8080`                   | Host port mapped to the dashboard.                                 |
| `API_BASE_URL`      | `http://localhost:7099`  | URL the **browser** uses to reach the API (see below).             |

### Why `API_BASE_URL` is a host URL, not `http://server:7099`

The dashboard is WebAssembly — it runs in the visitor's **browser**, not in the
Docker network. So the API URL it calls must be reachable from the browser. The
dashboard image reads `API_BASE_URL` at container start and writes it into
`wwwroot/appsettings.json`, so a single prebuilt image can target any server
without a rebuild. For a real deployment behind a domain, set e.g.
`API_BASE_URL=https://avizoro.example.com`.

## Building the images individually

```bash
docker build -f docker/server.Dockerfile    -t avizoro-server    .
docker build -f docker/dashboard.Dockerfile -t avizoro-dashboard .
```

(The build context is the repository root in both cases.)

## Switching database provider

The compose file uses PostgreSQL. To run the server against SQL Server instead,
change the `server` service's env: `Database__Provider=SqlServer` and point
`ConnectionStrings__Events` at your SQL Server instance. SQLite is intended for
local `dotnet run`, not the containerized multi-service stack.

## Production notes

- Put the server behind TLS (a reverse proxy such as Caddy/Traefik/nginx) and set
  `API_BASE_URL` to the public `https://` URL.
- Change `POSTGRES_PASSWORD` and use a strong `AVIZORO_READ_TOKEN`.
- Ingest (`POST /events`) is intentionally open — the client project key is a
  public routing id, not a secret. Only the read side is token-gated.
