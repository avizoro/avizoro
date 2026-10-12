using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Server;
using Avizoro.Server.Alerts;
using Avizoro.Server.Auth;
using Avizoro.Server.Contracts;
using Avizoro.Server.Data;
using Avizoro.Server.Feedback;
using Avizoro.Server.Ingest;
using Avizoro.Server.Integrations;
using Avizoro.Server.Performance;
using Avizoro.Server.Projects;
using Avizoro.Server.Releases;
using Avizoro.Server.Retention;
using Avizoro.Server.Search;
using Avizoro.Server.Sessions;
using Avizoro.Server.Symbols;
using Avizoro.Server.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

const string DashboardCors = "dashboard";
const string IngestRateLimit = "ingest";
const string AdminOnly = "admin-only";

var builder = WebApplication.CreateBuilder(args);

// Accept (and emit) enums as strings so third-party clients can post
// "eventType": "Exception" as well as the numeric form. Reading also still
// accepts numbers, so existing clients keep working.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Persistence: EF Core. Provider is config-driven ("Database:Provider" =
// Sqlite | PostgreSql | SqlServer), defaulting to SQLite (zero-config).
// Each provider has its own migrations assembly, selected by name here.
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
var connectionString = builder.Configuration.GetConnectionString("Events");

builder.Services.AddDbContext<AvizoroDbContext>(options =>
{
    switch (provider.ToLowerInvariant())
    {
        case "postgres":
        case "postgresql":
            // Also accepts a postgres:// URL, or DATABASE_URL, as PaaS platforms provide.
            options.UseNpgsql(
                Avizoro.Server.Hosting.ConnectionStrings.PostgreSql(builder.Configuration)
                    ?? throw new InvalidOperationException("ConnectionStrings:Events (or DATABASE_URL) is required for PostgreSql."),
                x => x.MigrationsAssembly("Avizoro.Server.Migrations.PostgreSql"));
            break;

        case "sqlserver":
        case "mssql":
            options.UseSqlServer(
                connectionString ?? throw new InvalidOperationException("ConnectionStrings:Events is required for SqlServer."),
                x => x.MigrationsAssembly("Avizoro.Server.Migrations.SqlServer"));
            break;

        default:
            options.UseSqlite(
                Avizoro.Server.Hosting.ConnectionStrings.EnsureSqliteDirectory(
                    Avizoro.Server.Hosting.ConnectionStrings.PreferLegacySqliteFile(connectionString ?? "Data Source=avizoro.db")),
                x => x.MigrationsAssembly("Avizoro.Server.Migrations.Sqlite"));
            break;
    }
});

builder.Services.AddOpenApi();

// Health (/health/live, /health/ready) and the server's own metrics. Metrics are
// exported via OpenTelemetry: Prometheus scrape endpoint at /metrics (on by
// default, admin-token protected) and/or OTLP (metrics + traces) when
// Telemetry:Otlp:Endpoint is set.
builder.Services.AddSingleton<ServerMetrics>();
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database", tags: [HealthEndpoints.ReadyTag]);

var prometheusEnabled = builder.Configuration.GetValue("Telemetry:Prometheus:Enabled", true);
var otlpEndpoint = builder.Configuration["Telemetry:Otlp:Endpoint"];
if (prometheusEnabled || !string.IsNullOrWhiteSpace(otlpEndpoint))
{
    var otel = builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("avizoro-server"))
        .WithMetrics(metrics =>
        {
            metrics.AddMeter(ServerMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation();
            if (prometheusEnabled)
                metrics.AddPrometheusExporter();
            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
        });

    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        otel.WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)));
}
builder.Services.AddSignalR();
builder.Services.AddScoped<EventIngestor>();
builder.Services.AddScoped<SessionIngestor>();
builder.Services.AddScoped<TimelineIngestor>();
builder.Services.AddScoped<IssueBackfill>();
builder.Services.AddScoped<Avizoro.Server.Demo.DemoSeeder>();
builder.Services.AddSingleton<SymbolCache>();
builder.Services.AddScoped<Symbolicator>();

// Alerts: rules are evaluated at ingest (AlertEvaluator) and delivered off the
// request path by a background dispatcher, so slow webhooks never delay ingest.
builder.Services.AddHttpClient(AlertSender.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<AlertQueue>();
builder.Services.AddSingleton<AlertSender>();
builder.Services.AddScoped<AlertEvaluator>();
builder.Services.AddHostedService<AlertDispatcher>();

// Outbound calls to GitHub/Jira when creating external issues.
builder.Services.AddHttpClient(IssueTrackerEndpoints.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));

// Data retention: a background service prunes events/screenshots/issues older
// than Retention:Days on a Retention:SweepHours cadence. Off by default (opt in
// via Retention:Enabled). The sweeper is scoped (it needs the DbContext).
builder.Services.AddScoped<RetentionSweeper>();
builder.Services.AddHostedService<RetentionService>();

// Read-side auth: a single shared admin token gates every query endpoint and the
// SignalR hub. Ingest (POST /events) stays open by design — the client's project
// key is a public routing id, not a secret. See ReadTokenAuthHandler.
builder.Services
    .AddAuthentication(ReadTokenDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ReadTokenAuthHandler>(ReadTokenDefaults.Scheme, null);
builder.Services.AddAuthorization(options =>
{
    // Project management is admin-only; a project-scoped read key can't manage tenants.
    options.AddPolicy(AdminOnly, policy =>
        policy.RequireClaim(ReadTokenDefaults.RoleClaim, ReadTokenDefaults.RoleAdmin));
});

// The Blazor WASM dashboard is served from its own origin, so it needs CORS.
// Auth is via bearer token (no cookies/credentials), so any origin may call.
builder.Services.AddCors(options =>
{
    options.AddPolicy(DashboardCors, policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// Ingest is unauthenticated by design, so it gets a per-client-IP rate limit to
// blunt floods/abuse. Only the POST ingest endpoints opt in (via
// RequireRateLimiting); the token-gated read side is left untouched. Config:
// RateLimit:Ingest:{Enabled,PermitLimit,WindowSeconds,QueueLimit} is read per
// request (so it can be overridden without a rebuild). A throttled client gets
// 429 + Retry-After; the SDK's store-and-forward defers those events (429 is
// treated as transient), so nothing is lost.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        return ValueTask.CompletedTask;
    };

    options.AddPolicy(IngestRateLimit, httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue("RateLimit:Ingest:Enabled", true))
            return RateLimitPartition.GetNoLimiter("disabled");

        var clientKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(clientKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = config.GetValue("RateLimit:Ingest:PermitLimit", 300),
            Window = TimeSpan.FromSeconds(config.GetValue("RateLimit:Ingest:WindowSeconds", 60)),
            QueueLimit = config.GetValue("RateLimit:Ingest:QueueLimit", 0),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
    });
});

var app = builder.Build();

app.UseCors(DashboardCors);

// Public demo instances: Demo:ReadOnly rejects every write (ingest, triage, admin)
// while reads and the live SignalR feed keep working.
if (app.Configuration.GetValue("Demo:ReadOnly", false))
{
    app.Use(async (context, next) =>
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) ||
            context.Request.Path.StartsWithSegments("/hubs"))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(new { error = "This is a read-only demo instance." });
    });
}
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Apply pending migrations on startup (creates the schema on first run).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AvizoroDbContext>();
    db.Database.Migrate();

    // Demo:Seed fills an EMPTY database with two weeks of sample data (a
    // "demo-shop" project with releases, issues, sessions, spans and feedback).
    if (app.Configuration.GetValue("Demo:Seed", false))
        await scope.ServiceProvider.GetRequiredService<Avizoro.Server.Demo.DemoSeeder>().SeedIfEmptyAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Ingest -----------------------------------------------------------------
app.MapPost("/events", async (AvizoroEvent avizoroEvent, HttpRequest request, AvizoroDbContext db, EventIngestor ingestor, SessionIngestor sessions, TimelineIngestor timeline, IConfiguration config, ServerMetrics metrics) =>
{
    // 1.x SDKs (published as IntelligenceKit) still send the old header name.
    var projectKey = request.Headers["X-Avizoro-Key"].ToString();
    if (string.IsNullOrEmpty(projectKey))
        projectKey = request.Headers["X-IntelligenceKit-Key"].ToString();

    // Reject events for unknown projects: the (projectId, projectKey) pair from the
    // DSN must match a registered project. Opt-out via Ingest:RequireKnownProject.
    if (config.GetValue("Ingest:RequireKnownProject", true))
    {
        var known = await db.Projects.AsNoTracking().AnyAsync(p =>
            p.ProjectId == avizoroEvent.ProjectId && p.ProjectKey == projectKey);
        if (!known)
        {
            metrics.EventRejected("unknown_project");
            return Results.NotFound(new { error = "Unknown project. Register it via POST /admin/projects." });
        }
    }

    // User feedback about an earlier event.
    if (avizoroEvent.EventType == EventType.Feedback)
    {
        if (avizoroEvent.Feedback is not { EventId: var fid, Comments: var comments } || fid == Guid.Empty || string.IsNullOrWhiteSpace(comments))
            return Results.BadRequest("Feedback events need 'feedback.eventId' and 'feedback.comments'.");

        await FeedbackEndpoints.IngestAsync(db, avizoroEvent);
        metrics.EventIngested("Feedback", avizoroEvent.ProjectId);
        return Results.Accepted();
    }

    // SDK performance batches are stored as spans, not issues.
    if (avizoroEvent.EventType == EventType.Performance && avizoroEvent.Spans is { Count: > 0 })
    {
        await PerformanceEndpoints.IngestAsync(db, avizoroEvent);
        metrics.SpansIngested(avizoroEvent.Spans.Count);
        return Results.Accepted();
    }

    // Session updates feed release health, not issues.
    if (avizoroEvent.EventType == EventType.Session)
    {
        if (avizoroEvent.Session is null)
            return Results.BadRequest("Session events need a 'session' payload.");

        await sessions.IngestAsync(avizoroEvent);
        metrics.SessionUpdate(avizoroEvent.Session.Status.ToString());
        return Results.Accepted();
    }

    // Session timeline batches (EnableSessionTimeline) are stored per session, not as issues.
    if (avizoroEvent.EventType == EventType.Timeline)
    {
        if (avizoroEvent.SessionId is null || avizoroEvent.SessionId == Guid.Empty || avizoroEvent.Breadcrumbs.Count == 0)
            return Results.BadRequest("Timeline events need 'sessionId' and at least one breadcrumb.");

        var stored = await timeline.IngestAsync(avizoroEvent);
        metrics.TimelineEntriesIngested(stored.Stored);
        return Results.Accepted();
    }

    var result = await ingestor.IngestAsync(avizoroEvent, projectKey);
    if (result.Duplicate)
        return Results.Ok(new { Id = avizoroEvent.Id });

    metrics.EventIngested(result.Event!.EventType, result.Event.ProjectId);
    if (result.Change != IssueChange.Existing)
        metrics.IssueChanged(result.Change.ToString());
    return Results.Created($"/events/{result.Event.Id}", new { result.Event.Id });
}).RequireRateLimiting(IngestRateLimit)
  .AddEndpointFilter(async (context, next) =>
  {
      var started = System.Diagnostics.Stopwatch.GetTimestamp();
      try
      {
          return await next(context);
      }
      finally
      {
          context.HttpContext.RequestServices.GetRequiredService<ServerMetrics>()
              .IngestDuration(System.Diagnostics.Stopwatch.GetElapsedTime(started));
      }
  });

// Query ------------------------------------------------------------------
// Search: every EventFilter field is an optional query parameter (q, level,
// release, environment, platform, userId, operatingSystem, deviceModel,
// tag=key:value (repeatable), from, to, projectId, eventType).
app.MapGet("/events", async (AvizoroDbContext db, ClaimsPrincipal user, [AsParameters] EventFilter filter, int skip = 0, int take = 50) =>
{
    take = Math.Clamp(take, 1, 200);

    var query = EventSearch.Apply(db.Events.AsNoTracking(), filter, user.ProjectScope());

    var total = await query.CountAsync();

    var items = await query
        .OrderByDescending(e => e.ReceivedAt)
        .Skip(skip)
        .Take(take)
        .Select(e => new EventSummary(
            e.Id, e.ProjectId, e.ApplicationName, e.ApplicationVersion,
            e.Environment, e.Platform, e.DeviceName, e.EventType,
            e.Level, e.UserId,
            e.ExceptionType, e.ExceptionMessage, e.Message, e.Timestamp, e.ReceivedAt, e.SessionId))
        .ToListAsync();

    return Results.Ok(new PagedResult<EventSummary>(total, skip, take, items));
}).RequireAuthorization();

// Exceptions are symbolicated on read (PDB file:line, R8 retrace) when matching
// symbols were uploaded — see Symbols/. The stored event itself is untouched.
app.MapGet("/events/{id:guid}", async (Guid id, AvizoroDbContext db, ClaimsPrincipal user, Symbolicator symbolicator) =>
{
    var e = await db.Events.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    if (e is null)
        return Results.NotFound();

    // Don't leak another project's event to a scoped caller (404, not 403, so the
    // event's existence isn't disclosed).
    var scope = user.ProjectScope();
    if (scope is not null && e.ProjectId != scope)
        return Results.NotFound();

    var hasScreenshot = await db.Screenshots.AsNoTracking().AnyAsync(s => s.EventId == id);

    var (exception, symbolicated) = await symbolicator.SymbolicateAsync(
        e.ExceptionJson is null ? null : JsonSerializer.Deserialize<ExceptionInfo>(e.ExceptionJson),
        e.ProjectId, e.Release);

    var detail = new EventDetail(
        e.Id, e.ProjectId, e.ProjectKey,
        e.ApplicationName, e.ApplicationVersion,
        e.Environment, e.Release,
        e.Platform, e.DeviceName, e.DeviceModel, e.Manufacturer, e.OperatingSystem,
        e.UserId,
        e.EventType, e.Level, e.Message,
        exception,
        e.DeviceRuntimeJson is null ? null : JsonSerializer.Deserialize<DeviceRuntime>(e.DeviceRuntimeJson),
        e.BreadcrumbsJson is null
            ? Array.Empty<Breadcrumb>()
            : JsonSerializer.Deserialize<List<Breadcrumb>>(e.BreadcrumbsJson) ?? new List<Breadcrumb>(),
        e.TagsJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(e.TagsJson),
        e.DataJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(e.DataJson),
        hasScreenshot,
        e.Timestamp, e.ReceivedAt,
        symbolicated,
        e.SessionId);

    return Results.Ok(detail);
}).RequireAuthorization();

// Screenshot blob for an event (the "last screen"). Uploaded separately from the
// event so image bytes never ride inside the JSON payload.
app.MapPost("/events/{id:guid}/screenshot", async (Guid id, HttpRequest request, AvizoroDbContext db) =>
{
    const long maxBytes = 2 * 1024 * 1024; // 2 MB cap

    if (request.ContentLength is > maxBytes)
        return Results.BadRequest("Screenshot too large.");

    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);
    if (buffer.Length == 0 || buffer.Length > maxBytes)
        return Results.BadRequest("Empty or oversized screenshot.");

    var bytes = buffer.ToArray();
    var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "image/jpeg" : request.ContentType;

    var existing = await db.Screenshots.FirstOrDefaultAsync(s => s.EventId == id);
    if (existing is null)
    {
        db.Screenshots.Add(new StoredScreenshot
        {
            EventId = id,
            Jpeg = bytes,
            ContentType = contentType,
            ReceivedAt = DateTime.UtcNow
        });
    }
    else
    {
        existing.Jpeg = bytes;
        existing.ContentType = contentType;
        existing.ReceivedAt = DateTime.UtcNow;
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { id });
}).RequireRateLimiting(IngestRateLimit);

app.MapGet("/events/{id:guid}/screenshot", async (Guid id, AvizoroDbContext db, ClaimsPrincipal user) =>
{
    var scope = user.ProjectScope();
    if (scope is not null)
    {
        // Screenshots belong to an event or, for on-demand timeline screenshots, to a timeline batch.
        var proj = await db.Events.AsNoTracking()
            .Where(e => e.Id == id).Select(e => e.ProjectId).FirstOrDefaultAsync()
            ?? await db.TimelineEntries.AsNoTracking()
            .Where(t => t.BatchId == id).Select(t => t.ProjectId).FirstOrDefaultAsync();
        if (proj != scope)
            return Results.NotFound();
    }

    var shot = await db.Screenshots.AsNoTracking().FirstOrDefaultAsync(s => s.EventId == id);
    return shot is null ? Results.NotFound() : Results.File(shot.Jpeg, shot.ContentType);
}).RequireAuthorization();

app.MapGet("/projects", async (AvizoroDbContext db, ClaimsPrincipal user) =>
{
    // Pull only the columns needed and group in memory: the SQLite provider
    // doesn't translate this GroupBy-with-filtered-count shape, and the
    // projected set is small (project id / type / timestamp).
    var baseQuery = db.Events.AsNoTracking().AsQueryable();
    var scope = user.ProjectScope();
    if (scope is not null)
        baseQuery = baseQuery.Where(e => e.ProjectId == scope);

    var rows = await baseQuery
        .Select(e => new { e.ProjectId, e.EventType, e.ReceivedAt })
        .ToListAsync();

    var projects = rows
        .GroupBy(e => e.ProjectId)
        .Select(g => new ProjectSummary(
            g.Key,
            g.Count(),
            g.Count(e => e.EventType == "Exception"),
            g.Max(e => e.ReceivedAt)))
        .OrderByDescending(p => p.LastEventAt)
        .ToList();

    return Results.Ok(projects);
}).RequireAuthorization();

// Events-per-hour time series for the dashboard chart. Buckets are filled with
// zeros for quiet hours so the series is continuous. Grouping is done in memory
// (the window is bounded, and it avoids provider-specific date functions).
app.MapGet("/stats/events-per-hour", async (AvizoroDbContext db, ClaimsPrincipal user, string? projectId, int hours = 24) =>
{
    hours = Math.Clamp(hours, 1, 168);

    var now = DateTime.UtcNow;
    var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
    var windowStart = currentHour.AddHours(-(hours - 1));

    var query = db.Events.AsNoTracking().Where(e => e.ReceivedAt >= windowStart);
    var projectFilter = user.ProjectScope() ?? projectId;
    if (!string.IsNullOrWhiteSpace(projectFilter))
        query = query.Where(e => e.ProjectId == projectFilter);

    var rows = await query.Select(e => new { e.ReceivedAt, e.EventType }).ToListAsync();

    var totals = new int[hours];
    var exceptions = new int[hours];
    foreach (var r in rows)
    {
        var index = (int)Math.Floor((r.ReceivedAt - windowStart).TotalHours);
        if (index < 0 || index >= hours)
            continue;

        totals[index]++;
        if (r.EventType == "Exception")
            exceptions[index]++;
    }

    var buckets = new TimeBucket[hours];
    for (var i = 0; i < hours; i++)
        buckets[i] = new TimeBucket(windowStart.AddHours(i), totals[i], exceptions[i]);

    return Results.Ok(buckets);
}).RequireAuthorization();

// Issues -----------------------------------------------------------------
// Grouped problems: one row per (project, fingerprint), newest activity first.
// ?status=Unresolved|Resolved|Ignored narrows the list; omitted = every status.
// ?release=X keeps only issues first seen in release X ("introduced in").
// ?q= searches title/culprit; ?assignedTo=, ?level=, ?eventType= narrow further.
app.MapGet("/issues", async (AvizoroDbContext db, ClaimsPrincipal user, string? projectId, string? status, string? release,
    string? q, string? assignedTo, string? level, string? eventType, int skip = 0, int take = 50) =>
{
    take = Math.Clamp(take, 1, 200);

    var projectFilter = user.ProjectScope() ?? projectId;

    var query = db.Issues.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(projectFilter))
        query = query.Where(i => i.ProjectId == projectFilter);

    if (!string.IsNullOrWhiteSpace(status))
    {
        var normalized = IssueStatuses.Normalize(status);
        if (normalized is null)
            return Results.BadRequest($"Unknown status '{status}'. Use one of: {string.Join(", ", IssueStatuses.All)}.");
        query = query.Where(i => i.Status == normalized);
    }

    if (!string.IsNullOrWhiteSpace(release))
        query = query.Where(i => i.FirstRelease == release);
    if (!string.IsNullOrWhiteSpace(q))
        query = query.Where(i => i.Title.Contains(q) || (i.Culprit != null && i.Culprit.Contains(q)));
    if (!string.IsNullOrWhiteSpace(assignedTo))
        query = query.Where(i => i.AssignedTo == assignedTo);
    if (!string.IsNullOrWhiteSpace(level))
        query = query.Where(i => i.Level == level);
    if (!string.IsNullOrWhiteSpace(eventType))
        query = query.Where(i => i.EventType == eventType);

    var total = await query.CountAsync();

    var issues = await query
        .OrderByDescending(i => i.LastSeen)
        .Skip(skip)
        .Take(take)
        .ToListAsync();

    // Trend: occurrences in the last hour vs the hour before, per fingerprint.
    // Bounded to a 2-hour window and grouped in memory (small, provider-neutral).
    var now = DateTime.UtcNow;
    var recentStart = now.AddHours(-1);
    var previousStart = now.AddHours(-2);

    var trendQuery = db.Events.AsNoTracking().Where(e => e.ReceivedAt >= previousStart);
    if (!string.IsNullOrWhiteSpace(projectFilter))
        trendQuery = trendQuery.Where(e => e.ProjectId == projectFilter);

    var trendRows = await trendQuery.Select(e => new { e.Fingerprint, e.ReceivedAt }).ToListAsync();

    var recent = new Dictionary<string, int>();
    var previous = new Dictionary<string, int>();
    foreach (var r in trendRows)
    {
        var bucket = r.ReceivedAt >= recentStart ? recent : previous;
        bucket[r.Fingerprint] = bucket.GetValueOrDefault(r.Fingerprint) + 1;
    }

    var items = issues.Select(i => i.ToSummary(
        recent.GetValueOrDefault(i.Fingerprint), previous.GetValueOrDefault(i.Fingerprint))).ToList();

    return Results.Ok(new PagedResult<IssueSummary>(total, skip, take, items));
}).RequireAuthorization();

app.MapGet("/issues/{id:guid}", async (Guid id, AvizoroDbContext db, ClaimsPrincipal user) =>
{
    var i = await db.Issues.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    if (i is null)
        return Results.NotFound();

    var scope = user.ProjectScope();
    if (scope is not null && i.ProjectId != scope)
        return Results.NotFound();

    var now = DateTime.UtcNow;
    var recentStart = now.AddHours(-1);
    var previousStart = now.AddHours(-2);

    var recent = await db.Events.AsNoTracking().CountAsync(e =>
        e.ProjectId == i.ProjectId && e.Fingerprint == i.Fingerprint && e.ReceivedAt >= recentStart);
    var previous = await db.Events.AsNoTracking().CountAsync(e =>
        e.ProjectId == i.ProjectId && e.Fingerprint == i.Fingerprint &&
        e.ReceivedAt >= previousStart && e.ReceivedAt < recentStart);

    return Results.Ok(i.ToSummary(recent, previous));
}).RequireAuthorization();

// Triage: change status (resolve / ignore / reopen) and/or the assignee. A scoped
// read key may triage its own project's issues. Resolving clears the regression
// flag; reopening by hand is not a regression.
app.MapMethods("/issues/{id:guid}", ["PATCH"], async (Guid id, UpdateIssueRequest req, AvizoroDbContext db, ClaimsPrincipal user, IHubContext<EventsHub> hub) =>
{
    var issue = await db.Issues.FirstOrDefaultAsync(x => x.Id == id);
    if (issue is null)
        return Results.NotFound();

    var scope = user.ProjectScope();
    if (scope is not null && issue.ProjectId != scope)
        return Results.NotFound();

    if (req.Status is not null)
    {
        var status = IssueStatuses.Normalize(req.Status);
        if (status is null)
            return Results.BadRequest($"Unknown status '{req.Status}'. Use one of: {string.Join(", ", IssueStatuses.All)}.");

        if (status == IssueStatuses.Resolved)
        {
            issue.ResolvedAt = DateTime.UtcNow;
            issue.ResolvedInRelease = string.IsNullOrWhiteSpace(req.ResolvedInRelease) ? null : req.ResolvedInRelease.Trim();
            issue.IsRegression = false;
        }
        else
        {
            issue.ResolvedAt = null;
            issue.ResolvedInRelease = null;
        }

        issue.Status = status;
    }

    if (req.AssignedTo is not null)
        issue.AssignedTo = string.IsNullOrWhiteSpace(req.AssignedTo) ? null : req.AssignedTo.Trim();

    await db.SaveChangesAsync();

    var summary = issue.ToSummary();
    await hub.Clients.Groups("admins", $"project:{issue.ProjectId}").SendAsync("issueUpserted", summary);
    return Results.Ok(summary);
}).RequireAuthorization();

app.MapGet("/issues/{id:guid}/events", async (Guid id, AvizoroDbContext db, ClaimsPrincipal user, int skip = 0, int take = 50) =>
{
    take = Math.Clamp(take, 1, 200);

    var issue = await db.Issues.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
    if (issue is null)
        return Results.NotFound();

    var scope = user.ProjectScope();
    if (scope is not null && issue.ProjectId != scope)
        return Results.NotFound();

    var query = db.Events.AsNoTracking()
        .Where(e => e.ProjectId == issue.ProjectId && e.Fingerprint == issue.Fingerprint);

    var total = await query.CountAsync();

    var items = await query
        .OrderByDescending(e => e.ReceivedAt)
        .Skip(skip)
        .Take(take)
        .Select(e => new EventSummary(
            e.Id, e.ProjectId, e.ApplicationName, e.ApplicationVersion,
            e.Environment, e.Platform, e.DeviceName, e.EventType,
            e.Level, e.UserId,
            e.ExceptionType, e.ExceptionMessage, e.Message, e.Timestamp, e.ReceivedAt, e.SessionId))
        .ToListAsync();

    return Results.Ok(new PagedResult<EventSummary>(total, skip, take, items));
}).RequireAuthorization();

// Project administration (tenants) --------------------------------------
// Admin-only: create/list/rotate/delete projects. Creating or rotating returns
// the read key ONCE (only its hash is stored).
app.MapPost("/admin/projects", async (CreateProjectRequest req, AvizoroDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(req.ProjectId))
        return Results.BadRequest("projectId is required.");

    if (await db.Projects.AnyAsync(p => p.ProjectId == req.ProjectId))
        return Results.Conflict($"Project '{req.ProjectId}' already exists.");

    var readKey = ProjectKeys.NewReadKey();
    var project = new Project
    {
        Id = Guid.NewGuid(),
        ProjectId = req.ProjectId,
        ProjectKey = string.IsNullOrWhiteSpace(req.ProjectKey) ? ProjectKeys.NewProjectKey() : req.ProjectKey!,
        Name = req.Name ?? string.Empty,
        ReadKeyHash = ProjectKeys.Hash(readKey),
        CreatedAt = DateTime.UtcNow,
    };

    db.Projects.Add(project);
    await db.SaveChangesAsync();

    return Results.Created($"/admin/projects/{project.Id}", new ProjectCredentials(
        project.Id, project.ProjectId, project.ProjectKey, project.Name, project.CreatedAt, readKey));
}).RequireAuthorization(AdminOnly);

app.MapGet("/admin/projects", async (AvizoroDbContext db) =>
{
    var projects = await db.Projects.AsNoTracking()
        .OrderBy(p => p.ProjectId)
        .Select(p => new ProjectInfo(p.Id, p.ProjectId, p.ProjectKey, p.Name, p.CreatedAt))
        .ToListAsync();
    return Results.Ok(projects);
}).RequireAuthorization(AdminOnly);

app.MapPost("/admin/projects/{id:guid}/rotate-key", async (Guid id, AvizoroDbContext db) =>
{
    var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id);
    if (project is null)
        return Results.NotFound();

    var readKey = ProjectKeys.NewReadKey();
    project.ReadKeyHash = ProjectKeys.Hash(readKey);
    await db.SaveChangesAsync();

    return Results.Ok(new ProjectCredentials(
        project.Id, project.ProjectId, project.ProjectKey, project.Name, project.CreatedAt, readKey));
}).RequireAuthorization(AdminOnly);

// Groups events stored before issue grouping existed. Safe to re-run.
app.MapPost("/admin/issues/backfill", async (IssueBackfill backfill, CancellationToken ct) =>
    Results.Ok(await backfill.RunAsync(ct))).RequireAuthorization(AdminOnly);

app.MapDelete("/admin/projects/{id:guid}", async (Guid id, AvizoroDbContext db) =>
{
    var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id);
    if (project is null)
        return Results.NotFound();

    db.Projects.Remove(project);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization(AdminOnly);

app.MapSessionEndpoints();
app.MapSessionTimelineEndpoints();
app.MapReleaseEndpoints();
app.MapPerformanceEndpoints();
app.MapFeedbackEndpoints();
app.MapDistributionEndpoints();
app.MapUserEndpoints();
app.MapExportEndpoints();
app.MapIssueTrackerEndpoints();
app.MapAlertEndpoints(AdminOnly);
app.MapSymbolEndpoints(AdminOnly);

app.MapHealthEndpoints();
if (prometheusEnabled)
{
    // Tags carry project ids, so the scrape endpoint is admin-only unless opted out
    // (Prometheus supports bearer tokens: authorization.credentials in scrape_configs).
    var scrape = app.MapPrometheusScrapingEndpoint("/metrics");
    if (app.Configuration.GetValue("Telemetry:Prometheus:RequireAuth", true))
        scrape.RequireAuthorization(AdminOnly);
}

app.MapHub<EventsHub>("/hubs/events").RequireAuthorization();

app.Run();

// Exposed so the integration test project can host the app via
// WebApplicationFactory<Program>. Top-level programs otherwise emit an internal
// Program class the test assembly can't reference.
public partial class Program { }
