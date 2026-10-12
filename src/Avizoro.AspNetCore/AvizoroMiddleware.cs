using System.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Avizoro.AspNetCore;

/// <summary>
/// Per-request instrumentation, inserted at the very start of the pipeline by
/// <see cref="AvizoroStartupFilter"/>:
/// <list type="bullet">
///   <item>pushes an <see cref="AvizoroScope"/> so breadcrumbs/tags of concurrent
///   requests stay separate, tagged with method and route;</item>
///   <item>captures exceptions that escape the pipeline, with request context
///   (exceptions an inner handler logs are captured by the logger provider instead —
///   inside this scope, so they carry the same context);</item>
///   <item>records an <c>http.server</c> span named after the route template
///   (<c>GET /orders/{id}</c>), so timings aggregate per endpoint.</item>
/// </list>
/// </summary>
public sealed class AvizoroMiddleware
{
    public const string ServerSpanOperation = "http.server";

    private readonly RequestDelegate _next;
    private readonly IAvizoro _kit;
    private readonly IPerformanceMonitor? _performance;

    public AvizoroMiddleware(RequestDelegate next, IAvizoro kit, IPerformanceMonitor? performance = null)
    {
        _next = next;
        _kit = kit;
        _performance = performance;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        using var scope = AvizoroScope.Push();
        scope.SetTag("http.method", context.Request.Method);

        var watch = Stopwatch.StartNew();
        var failed = false;
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            failed = true;
            if (!ExceptionCapture.IsCaptured(ex))
            {
                ExceptionCapture.MarkCaptured(ex);
                await CaptureAsync(context, ex);
            }
            throw;
        }
        finally
        {
            RecordSpan(context, watch.Elapsed, failed);
        }
    }

    private async Task CaptureAsync(HttpContext context, Exception ex)
    {
        try
        {
            var route = RouteTemplate(context);
            var e = new AvizoroEvent
            {
                EventType = EventType.Exception,
                Level = SeverityLevel.Error,
                Exception = ExceptionInfo.FromException(ex),
                Tags =
                {
                    ["mechanism"] = "aspnetcore",
                    ["http.method"] = context.Request.Method,
                },
                Data =
                {
                    ["request.path"] = context.Request.Path.Value,
                    ["request.trace_id"] = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier,
                    ["request.user_agent"] = context.Request.Headers.UserAgent.ToString(),
                }
            };
            if (route is not null)
                e.Tags["http.route"] = route;

            await _kit.TrackAsync(e);
        }
        catch
        {
            // Never mask the original exception.
        }
    }

    private void RecordSpan(HttpContext context, TimeSpan elapsed, bool failed)
    {
        if (_performance is null)
            return;

        try
        {
            var status = failed ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
            var route = RouteTemplate(context) ?? "(unmatched)";
            _performance.Record(new PerformanceSpan
            {
                Operation = ServerSpanOperation,
                Name = $"{context.Request.Method} {route}",
                Start = DateTime.UtcNow - elapsed,
                DurationMs = Math.Round(elapsed.TotalMilliseconds, 3),
                StatusCode = status,
                Success = !failed && status < 500
            });
        }
        catch
        {
        }
    }

    /// <summary>The matched endpoint's route template, e.g. "/orders/{id}". Null when unrouted.</summary>
    private static string? RouteTemplate(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } raw })
            return raw.StartsWith('/') ? raw : "/" + raw;
        return null;
    }
}

/// <summary>Adds <see cref="AvizoroMiddleware"/> first in the pipeline — no <c>app.Use…</c> needed.</summary>
internal sealed class AvizoroStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<AvizoroMiddleware>();
        next(app);
    };
}
