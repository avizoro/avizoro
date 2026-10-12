using System.Net;
using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;

namespace Avizoro.Core.Tests;

public class PerformanceTests
{
    private readonly CallLog _log = new();

    private (PerformanceMonitor Monitor, FakeEventStore Store) Create(double sampleRate = 1.0)
    {
        var store = new FakeEventStore(_log);
        var monitor = new PerformanceMonitor(store, new FakeUploader(_log),
            new AvizoroOptions { ProjectId = "p", ApplicationVersion = "3.0", PerformanceSampleRate = sampleRate, PerformanceFlushInterval = TimeSpan.Zero },
            new FakeDeviceContextProvider());
        return (monitor, store);
    }

    [Fact]
    public async Task Spans_AreBatchedIntoOnePerformanceEvent()
    {
        var (monitor, store) = Create();
        using (monitor.StartSpan(SpanOperations.PageLoad, "MainPage")) { }
        monitor.Record(new PerformanceSpan { Operation = SpanOperations.AppStart, Name = "cold", DurationMs = 812 });

        Assert.Empty(store.Saved);
        await monitor.FlushAsync();

        var e = Assert.Single(store.Saved);
        Assert.Equal(EventType.Performance, e.EventType);
        Assert.Equal("p", e.ProjectId);
        Assert.Equal("3.0", e.Release);
        Assert.Equal(2, e.Spans!.Count);
        Assert.Equal("MainPage", e.Spans[0].Name);
        Assert.True(e.Spans[0].DurationMs >= 0);
    }

    [Fact]
    public async Task EmptyFlush_SendsNothing()
    {
        var (monitor, store) = Create();
        await monitor.FlushAsync();
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task FullBatch_FlushesAutomatically()
    {
        var (monitor, store) = Create();
        for (var i = 0; i < PerformanceMonitor.BatchSize; i++)
            monitor.Record(new PerformanceSpan { Operation = "x", Name = "n" });

        for (var i = 0; i < 50 && store.Saved.Count == 0; i++)
            await Task.Delay(20);

        Assert.Equal(PerformanceMonitor.BatchSize, store.Saved.Single().Spans!.Count);
    }

    [Fact]
    public void ZeroSampleRate_DropsSpans()
    {
        var (monitor, _) = Create(sampleRate: 0);
        monitor.Record(new PerformanceSpan());
        Assert.Equal(0, monitor.Pending);
    }

    [Fact]
    public async Task HttpHandler_RecordsSpanAndBreadcrumb_WithNormalizedUrl()
    {
        var (monitor, store) = Create();
        var kit = new BreadcrumbKit();
        using var client = new HttpClient(new AvizoroHttpHandler(kit, monitor)
        {
            InnerHandler = new StubHandler(HttpStatusCode.ServiceUnavailable)
        });

        await client.GetAsync("https://api.example.com/orders/12345/items?token=secret");
        await monitor.FlushAsync();

        var span = store.Saved.Single().Spans!.Single();
        Assert.Equal(SpanOperations.Http, span.Operation);
        Assert.Equal("GET api.example.com/orders/{id}/items", span.Name);
        Assert.Equal(503, span.StatusCode);
        Assert.False(span.Success);

        var (message, category, level) = Assert.Single(kit.Crumbs);
        Assert.Equal("GET api.example.com/orders/{id}/items", message);
        Assert.Equal(BreadcrumbCategories.Http, category);
        Assert.Equal(SeverityLevel.Error, level);
    }

    [Fact]
    public async Task HttpHandler_RecordsFailures_AndRethrows()
    {
        var kit = new BreadcrumbKit();
        using var client = new HttpClient(new AvizoroHttpHandler(kit) { InnerHandler = new ThrowingHandler() });

        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://down.example.com/"));

        Assert.Equal(SeverityLevel.Error, kit.Crumbs.Single().Level);
    }

    [Theory]
    [InlineData("https://x.io/users/42", "x.io/users/{id}")]
    [InlineData("https://x.io/u/3f2504e0-4f89-11d3-9a0c-0305e82c3301/p", "x.io/u/{id}/p")]
    [InlineData("https://x.io/search?q=ana@example.com", "x.io/search")]
    [InlineData("https://x.io/v2/catalog", "x.io/v2/catalog")]
    public void Describe_NormalizesUrls(string url, string expected)
        => Assert.Equal(expected, AvizoroHttpHandler.Describe(new Uri(url)));

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("connection refused");
    }

    private sealed class BreadcrumbKit : IAvizoro
    {
        public List<(string Message, string Category, SeverityLevel Level)> Crumbs { get; } = new();

        public void AddBreadcrumb(string message, string category = BreadcrumbCategories.Custom,
            SeverityLevel level = SeverityLevel.Information, IDictionary<string, string>? data = null)
            => Crumbs.Add((message, category, level));

        public Task TrackAsync(AvizoroEvent avizoroEvent) => Task.CompletedTask;
        public Task TrackExceptionAsync(Exception exception) => Task.CompletedTask;
        public Task TrackExceptionAsync(ExceptionInfo exception) => Task.CompletedTask;
        public Task TrackLogAsync(SeverityLevel level, string message, IDictionary<string, string>? data = null) => Task.CompletedTask;
        public void SetUser(string? userId) { }
        public void SetTag(string key, string? value) { }
        public Task CaptureCrashAsync(ExceptionInfo exception) => Task.CompletedTask;
    }
}
