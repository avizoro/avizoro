using Avizoro.AspNetCore;
using Avizoro.Core.Enums;
using Avizoro.Core.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avizoro.Sdk.Tests;

public class AspNetCoreTests : IAsyncLifetime
{
    private readonly FakeIngest _ingest = new();
    private WebApplication _app = default!;
    private HttpClient _client = default!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.UseAvizoro("http://key@ik.test:7099/my-api", o =>
        {
            o.OfflineStorePath = TempDir.Create();
            o.EnableCrashHandlers = false;
        });
        builder.Services.AddHttpClient<IAvizoroClient, HttpAvizoroClient>()
            .ConfigurePrimaryHttpMessageHandler(() => _ingest);

        _app = builder.Build();
        _app.MapGet("/orders/{id:int}", (int id, ILogger<AspNetCoreTests> logger) =>
        {
            if (id == 0)
                throw new InvalidOperationException("order zero");
            if (id == 1)
                logger.LogError("Order {OrderId} could not be priced", id);
            return Results.Ok(id);
        });

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task UnhandledRequestException_IsCapturedOnce_WithRequestContext()
    {
        try
        {
            await _client.GetAsync("/orders/0");
        }
        catch (InvalidOperationException)
        {
            // TestServer surfaces the exception to the caller.
        }

        var e = await _ingest.WaitForAsync(x => x.Exception?.Message == "order zero", "request exception");
        Assert.Equal("aspnetcore", e.Tags["mechanism"]);
        Assert.Equal("/orders/{id:int}", e.Tags["http.route"]);
        Assert.Equal("GET", e.Tags["http.method"]);
        Assert.Equal("/orders/0", e.Data["request.path"]?.ToString());

        await Task.Delay(300);
        Assert.Single(_ingest.Events, x => x.Exception?.Message == "order zero");
    }

    [Fact]
    public async Task LoggedError_BecomesEvent_WithRequestScopeTags()
    {
        (await _client.GetAsync("/orders/1")).EnsureSuccessStatusCode();

        var e = await _ingest.WaitForAsync(x => x.Message == "Order 1 could not be priced", "logged error");
        Assert.Equal(EventType.Log, e.EventType);
        Assert.Equal("GET", e.Tags["http.method"]); // from the request scope
        Assert.Equal("Order {OrderId} could not be priced", e.Data["message_template"]?.ToString());
    }

    [Fact]
    public async Task Requests_AreTimedPerRoute()
    {
        (await _client.GetAsync("/orders/5")).EnsureSuccessStatusCode();
        (await _client.GetAsync("/orders/6")).EnsureSuccessStatusCode();

        await _app.Services.GetRequiredService<IPerformanceMonitor>().FlushAsync();

        var batch = await _ingest.WaitForAsync(x => x.EventType == EventType.Performance, "performance batch");
        var spans = batch.Spans!.Where(s => s.Operation == AvizoroMiddleware.ServerSpanOperation).ToList();
        Assert.Equal(2, spans.Count);
        Assert.All(spans, s => Assert.Equal("GET /orders/{id:int}", s.Name));
        Assert.All(spans, s => Assert.Equal(200, s.StatusCode));
    }

    [Fact]
    public async Task Sessions_AreOffForServers()
    {
        (await _client.GetAsync("/orders/5")).EnsureSuccessStatusCode();
        await Task.Delay(300);
        Assert.DoesNotContain(_ingest.Events, e => e.EventType == EventType.Session);
    }
}
