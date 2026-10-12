# Avizoro ingest/query backend.
# Build context is the repository root: docker build -f docker/server.Dockerfile .

# ---- build ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the product source (tests/samples are excluded via .dockerignore) and
# restore + publish the Server. Publishing it transitively builds Core,
# Server.Contracts, Server.Data and all three Server.Migrations.* provider
# assemblies (referenced by the Server project), so runtime Migrate() works for
# whichever provider is configured.
COPY src/ src/
RUN dotnet restore src/Avizoro.Server/Avizoro.Server.csproj
RUN dotnet publish src/Avizoro.Server/Avizoro.Server.csproj \
    -c Release -o /app/publish --no-restore

# ---- runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

LABEL org.opencontainers.image.title="Avizoro Server" \
      org.opencontainers.image.description="Self-hosted ingest/query backend for Avizoro (crash reporting & observability for .NET)." \
      org.opencontainers.image.source="https://github.com/avizoro/avizoro" \
      org.opencontainers.image.licenses="MIT"

# Listens on $PORT (default 7099, the project's http profile). PaaS platforms
# (Render, Railway, Cloud Run...) inject PORT; an explicit ASPNETCORE_URLS wins.
ENV PORT=7099 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=1

EXPOSE 7099
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=\"${ASPNETCORE_URLS:-http://+:${PORT}}\" exec dotnet Avizoro.Server.dll"]
