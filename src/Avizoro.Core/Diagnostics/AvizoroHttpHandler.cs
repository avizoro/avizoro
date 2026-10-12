using System.Text.RegularExpressions;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;

namespace Avizoro.Core.Diagnostics;

/// <summary>
/// <see cref="DelegatingHandler"/> that turns every outgoing HTTP request into an
/// <c>http</c> breadcrumb (method, URL, status, duration) and, when a
/// <see cref="IPerformanceMonitor"/> is available, a <see cref="SpanOperations.Http"/>
/// span. URLs are recorded without query string or fragment, and id-like path
/// segments are collapsed (<c>/orders/123</c> → <c>/orders/{id}</c>) so they
/// aggregate and don't leak identifiers.
/// <para>Add it with <c>services.AddHttpClient&lt;MyApi&gt;().AddAvizoroHandler()</c>,
/// or wrap it manually: <c>new HttpClient(new AvizoroHttpHandler(kit) { InnerHandler = new HttpClientHandler() })</c>.</para>
/// </summary>
public sealed partial class AvizoroHttpHandler : DelegatingHandler
{
    private readonly IAvizoro _kit;
    private readonly IPerformanceMonitor? _performance;

    public AvizoroHttpHandler(IAvizoro kit, IPerformanceMonitor? performance = null)
    {
        _kit = kit;
        _performance = performance;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var name = $"{request.Method.Method} {Describe(request.RequestUri)}";
        var span = _performance?.StartSpan(SpanOperations.Http, name);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            span?.Finish(success: status < 500, statusCode: status);
            Breadcrumb(name, status, watch.Elapsed, error: null, RequestSize(request), response.Content?.Headers.ContentLength);
            return response;
        }
        catch (Exception ex)
        {
            span?.Finish(success: false);
            Breadcrumb(name, null, watch.Elapsed, ex.GetType().Name, RequestSize(request), responseSize: null);
            throw;
        }
    }

    private void Breadcrumb(string name, int? status, TimeSpan elapsed, string? error, long? requestSize, long? responseSize)
    {
        try
        {
            var data = new Dictionary<string, string> { ["duration_ms"] = ((int)elapsed.TotalMilliseconds).ToString() };
            if (status is { } s)
                data["status_code"] = s.ToString();
            if (error is not null)
                data["error"] = error;
            // Sizes come from Content-Length only: bodies are never read.
            if (requestSize is { } rq)
                data["request_size"] = rq.ToString();
            if (responseSize is { } rs)
                data["response_size"] = rs.ToString();

            var level = error is not null || status >= 500 ? SeverityLevel.Error
                : status >= 400 ? SeverityLevel.Warning
                : SeverityLevel.Information;

            _kit.AddBreadcrumb(name, BreadcrumbCategories.Http, level, data);
        }
        catch
        {
            // Instrumentation must never affect the request.
        }
    }

    private static long? RequestSize(HttpRequestMessage request)
    {
        try
        {
            return request.Content?.Headers.ContentLength;
        }
        catch
        {
            return null;
        }
    }

    /// <summary><c>host/path</c> with query/fragment removed and id-like segments collapsed.</summary>
    public static string Describe(Uri? uri)
    {
        if (uri is null)
            return "(unknown)";
        if (!uri.IsAbsoluteUri)
            return NormalizePath(uri.OriginalString.Split('?', '#')[0]);

        return uri.Host + NormalizePath(uri.AbsolutePath);
    }

    private static string NormalizePath(string path)
        => IdSegment().Replace(path, "/{id}");

    // Digits-only, GUIDs, or long hex/base64-ish tokens between slashes.
    [GeneratedRegex(@"/(?:\d+|[0-9a-fA-F]{8}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{4}-?[0-9a-fA-F]{12}|[0-9a-fA-F]{16,}|[A-Za-z0-9_\-]{24,})(?=/|$)")]
    private static partial Regex IdSegment();
}
