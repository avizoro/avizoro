using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avizoro.Server.Performance;

namespace Avizoro.Server.Tests;

public class PerformanceTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public PerformanceTests(ServerAppFactory factory) => _factory = factory;

    private static Dictionary<string, object?> Batch(string project, params (string Op, string Name, double Ms, bool Ok)[] spans)
        => new()
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["projectId"] = project,
            ["release"] = "1.0",
            ["eventType"] = "Performance",
            ["timestamp"] = DateTime.UtcNow,
            ["spans"] = spans.Select(s => new Dictionary<string, object?>
            {
                ["operation"] = s.Op,
                ["name"] = s.Name,
                ["start"] = DateTime.UtcNow,
                ["durationMs"] = s.Ms,
                ["success"] = s.Ok,
            }).ToList()
        };

    [Fact]
    public async Task Batch_IsAccepted_AndSummarizedWithPercentiles()
    {
        const string project = "perf-summary";
        var client = _factory.CreateClient();

        var pageLoads = Enumerable.Range(1, 100).Select(i => ("ui.load", "MainPage", (double)i, true)).ToArray();
        var response = await client.PostAsJsonAsync("/events", Batch(project, pageLoads));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await client.PostAsJsonAsync("/events", Batch(project,
            ("http.client", "GET api.test/orders/{id}", 900, false),
            ("http.client", "GET api.test/orders/{id}", 100, true)));

        var summaries = await _factory.CreateAuthorizedClient()
            .GetFromJsonAsync<JsonElement>($"/performance?projectId={project}");
        var byName = summaries.EnumerateArray().ToDictionary(s => s.GetProperty("name").GetString()!);

        var page = byName["MainPage"];
        Assert.Equal(100, page.GetProperty("count").GetInt32());
        Assert.Equal(50, page.GetProperty("p50Ms").GetDouble());
        Assert.Equal(95, page.GetProperty("p95Ms").GetDouble());

        var http = byName["GET api.test/orders/{id}"];
        Assert.Equal(0.5, http.GetProperty("failureRate").GetDouble());
        Assert.Equal("http.client", http.GetProperty("operation").GetString());

        // Slowest p95 first.
        Assert.Equal("GET api.test/orders/{id}", summaries[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task OperationFilter_Narrows()
    {
        const string project = "perf-filter";
        await _factory.CreateClient().PostAsJsonAsync("/events", Batch(project,
            ("app.start", "cold", 1200, true), ("ui.load", "Settings", 80, true)));

        var summaries = await _factory.CreateAuthorizedClient()
            .GetFromJsonAsync<JsonElement>($"/performance?projectId={project}&operation=app.start");

        Assert.Equal("cold", summaries.EnumerateArray().Single().GetProperty("name").GetString());
    }

    [Fact]
    public async Task PerformanceBatch_DoesNotCreateIssues()
    {
        const string project = "perf-no-issues";
        await _factory.CreateClient().PostAsJsonAsync("/events", Batch(project, ("ui.load", "A", 10, true)));

        var issues = await _factory.CreateAuthorizedClient().GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        Assert.Equal(0, issues.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData(new double[] { 5 }, 0.95, 5)]
    [InlineData(new double[] { 1, 2, 3, 4 }, 0.5, 2)]
    [InlineData(new double[] { 1, 2, 3, 4 }, 0.75, 3)]
    public void Percentile_NearestRank(double[] values, double p, double expected)
        => Assert.Equal(expected, PerformanceEndpoints.Percentile(values, p));
}
