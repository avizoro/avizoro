# Avizoro Blazor WebAssembly dashboard, served as static files by nginx.
# Build context is the repository root: docker build -f docker/dashboard.Dockerfile .

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The wasm-tools workload is only *recommended* (size optimization); a plain
# publish produces a fully working app, so we skip it to keep the build lean.
COPY src/ src/
RUN dotnet restore src/Avizoro.Dashboard/Avizoro.Dashboard.csproj
RUN dotnet publish src/Avizoro.Dashboard/Avizoro.Dashboard.csproj \
    -c Release -o /app/publish --no-restore

# ---- runtime --------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

LABEL org.opencontainers.image.title="Avizoro Dashboard" \
      org.opencontainers.image.description="Blazor WebAssembly dashboard for Avizoro." \
      org.opencontainers.image.source="https://github.com/wilsonvargas/Avizoro" \
      org.opencontainers.image.licenses="MIT"

# SPA routing + WASM MIME/caching rules. Installed as a template: the nginx
# image substitutes environment variables (here ${PORT}, default 80, which PaaS
# platforms may inject) into /etc/nginx/templates/*.template at start.
ENV PORT=80
COPY docker/nginx.conf /etc/nginx/templates/default.conf.template

# Static site (the published Blazor output lives under wwwroot).
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

# Rewrites wwwroot/appsettings.json from $API_BASE_URL before nginx starts.
# The official nginx image runs every /docker-entrypoint.d/*.sh at container
# start, so no custom ENTRYPOINT is needed.
COPY docker/dashboard-entrypoint.sh /docker-entrypoint.d/40-avizoro-apibaseurl.sh
RUN chmod +x /docker-entrypoint.d/40-avizoro-apibaseurl.sh

EXPOSE 80
