using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class ReleaseHealthTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public ReleaseHealthTests(ServerAppFactory factory) => _factory = factory;

    private static Dictionary<string, object?> ExceptionIn(string project, string release, string type = "System.NullReferenceException")
    {
        var e = TestEvents.Exception(projectId: project, exceptionType: type);
        e["release"] = release;
        return e;
    }

    [Fact]
    public async Task Releases_ReportSessionsEventsAndNewIssues()
    {
        const string project = "rel-health";
        var client = _factory.CreateClient();

        // 1.0: two clean sessions, one existing issue. 2.0: one crashed + one clean
        // session, and it introduces a new issue.
        await client.PostAsJsonAsync("/events", SessionTests.SessionUpdate(project, Guid.NewGuid(), "Exited", 1, release: "1.0", distinctId: "a"));
        await client.PostAsJsonAsync("/events", SessionTests.SessionUpdate(project, Guid.NewGuid(), "Exited", 1, release: "1.0", distinctId: "b"));
        await client.PostAsJsonAsync("/events", SessionTests.SessionUpdate(project, Guid.NewGuid(), "Crashed", 1, release: "2.0", distinctId: "a"));
        await client.PostAsJsonAsync("/events", SessionTests.SessionUpdate(project, Guid.NewGuid(), "Exited", 1, release: "2.0", distinctId: "c"));

        await client.PostAsJsonAsync("/events", ExceptionIn(project, "1.0"));
        await client.PostAsJsonAsync("/events", ExceptionIn(project, "2.0"));
        await client.PostAsJsonAsync("/events", ExceptionIn(project, "2.0", "System.InvalidOperationException"));

        var releases = await _factory.CreateAuthorizedClient()
            .GetFromJsonAsync<JsonElement>($"/releases?projectId={project}");
        var byName = releases.EnumerateArray().ToDictionary(r => r.GetProperty("release").GetString()!);

        Assert.Equal(2, byName.Count);

        var v1 = byName["1.0"];
        Assert.Equal(2, v1.GetProperty("sessions").GetInt32());
        Assert.Equal(1.0, v1.GetProperty("crashFreeSessionRate").GetDouble());
        Assert.Equal(1, v1.GetProperty("events").GetInt32());
        Assert.Equal(1, v1.GetProperty("newIssues").GetInt32());
        Assert.Equal(0.5, v1.GetProperty("adoption").GetDouble());

        var v2 = byName["2.0"];
        Assert.Equal(0.5, v2.GetProperty("crashFreeSessionRate").GetDouble());
        Assert.Equal(2, v2.GetProperty("exceptions").GetInt32());
        Assert.Equal(1, v2.GetProperty("newIssues").GetInt32()); // the NRE was introduced in 1.0
    }

    [Fact]
    public async Task Issues_TrackFirstAndLastRelease_AndFilterByIntroducedRelease()
    {
        const string project = "rel-issues";
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/events", ExceptionIn(project, "3.1.0"));
        await client.PostAsJsonAsync("/events", ExceptionIn(project, "3.2.0"));
        await client.PostAsJsonAsync("/events", ExceptionIn(project, "3.2.0", "System.TimeoutException"));

        var authed = _factory.CreateAuthorizedClient();
        var introducedIn32 = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}&release=3.2.0");
        var only = introducedIn32.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("TimeoutException", only.GetProperty("title").GetString());

        var all = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        var nre = all.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("title").GetString() == "NullReferenceException");
        Assert.Equal("3.1.0", nre.GetProperty("firstRelease").GetString());
        Assert.Equal("3.2.0", nre.GetProperty("lastRelease").GetString());
    }
}
