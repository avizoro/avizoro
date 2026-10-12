using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class DemoTests : IDisposable
{
    private readonly ServerAppFactory _factory = ServerAppFactory.CreateWith(settings: new Dictionary<string, string?>
    {
        ["Demo:Seed"] = "true",
        ["Demo:ReadOnly"] = "true",
    });

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task DemoSeed_ProducesAFullyPopulatedInstance()
    {
        var authed = _factory.CreateAuthorizedClient();
        const string p = "demo-shop";

        var issues = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={p}&take=50");
        var titles = issues.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToList();
        Assert.Contains("NullReferenceException", titles);
        Assert.Contains("ApplicationNotResponding", titles);
        Assert.Contains(issues.GetProperty("items").EnumerateArray(), i => i.GetProperty("isRegression").GetBoolean());
        Assert.Contains(issues.GetProperty("items").EnumerateArray(), i => i.GetProperty("status").GetString() == "Resolved");

        var releases = await authed.GetFromJsonAsync<JsonElement>($"/releases?projectId={p}");
        var byRelease = releases.EnumerateArray().ToDictionary(r => r.GetProperty("release").GetString()!);
        Assert.Equal(3, byRelease.Count);
        // The story: 2.4.0 is the bad release, 2.4.1 fixes it.
        Assert.True(byRelease["2.4.0"].GetProperty("crashFreeSessionRate").GetDouble() <
                    byRelease["2.4.1"].GetProperty("crashFreeSessionRate").GetDouble());

        var perf = await authed.GetFromJsonAsync<JsonElement>($"/performance?projectId={p}&days=30");
        Assert.Contains(perf.EnumerateArray(), s => s.GetProperty("operation").GetString() == "app.start");

        var feedback = await authed.GetFromJsonAsync<JsonElement>($"/feedback?projectId={p}");
        Assert.True(feedback.GetProperty("total").GetInt32() > 0);

        var projects = await authed.GetFromJsonAsync<JsonElement>("/admin/projects");
        Assert.Contains(projects.EnumerateArray(), x => x.GetProperty("projectId").GetString() == p);
    }

    [Fact]
    public async Task DemoSeed_IncludesSessionTimelines_WithACrashOnOne()
    {
        var authed = _factory.CreateAuthorizedClient();

        var sessions = await authed.GetFromJsonAsync<JsonElement>("/sessions?projectId=demo-shop&withTimeline=true&take=50");
        var items = sessions.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.All(items, s => Assert.False(string.IsNullOrEmpty(s.GetProperty("deviceModel").GetString())));

        var crashed = items.First(s => s.GetProperty("status").GetString() == "Crashed").GetProperty("id").GetGuid();
        var timeline = await authed.GetFromJsonAsync<JsonElement>($"/sessions/{crashed}/timeline");
        var last = timeline.GetProperty("items").EnumerateArray().Last();
        Assert.Equal("event", last.GetProperty("kind").GetString());
        Assert.Equal("System.NullReferenceException", last.GetProperty("exceptionType").GetString());
    }

    [Fact]
    public async Task ReadOnly_RejectsWrites_ButServesReads()
    {
        var authed = _factory.CreateAuthorizedClient();
        var issueId = (await authed.GetFromJsonAsync<JsonElement>("/issues?projectId=demo-shop")).GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.Forbidden, (await authed.PatchAsJsonAsync($"/issues/{issueId}", new { status = "Ignored" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync($"/issues/{issueId}")).StatusCode);
    }
}
