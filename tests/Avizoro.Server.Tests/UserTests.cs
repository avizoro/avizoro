using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class UserTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public UserTests(ServerAppFactory factory) => _factory = factory;

    private static Dictionary<string, object?> For(string project, string? user, string type = "System.NullReferenceException")
    {
        var e = TestEvents.Exception(projectId: project, exceptionType: type);
        e["userId"] = user;
        return e;
    }

    [Fact]
    public async Task IssueUsers_ListsAffectedUsersWithCounts()
    {
        const string project = "users-issue";
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/events", For(project, "ana"));
        await client.PostAsJsonAsync("/events", For(project, "ana"));
        await client.PostAsJsonAsync("/events", For(project, "luis"));
        await client.PostAsJsonAsync("/events", For(project, null));

        var authed = _factory.CreateAuthorizedClient();
        var issueId = (await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}")).GetProperty("items")[0].GetProperty("id").GetGuid();
        var users = await authed.GetFromJsonAsync<JsonElement>($"/issues/{issueId}/users");

        var byId = users.EnumerateArray().ToDictionary(u => u.GetProperty("userId").GetString()!, u => u.GetProperty("events").GetInt32());
        Assert.Equal(2, byId.Count);
        Assert.Equal(2, byId["ana"]);
        Assert.Equal(1, byId["luis"]);
    }

    [Fact]
    public async Task UserTimeline_ShowsIssuesSessionsAndEvents()
    {
        const string project = "users-timeline";
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/events", For(project, "maria"));
        await client.PostAsJsonAsync("/events", For(project, "maria", "System.TimeoutException"));
        await client.PostAsJsonAsync("/events", For(project, "someone-else"));
        await client.PostAsJsonAsync("/events", SessionTests.SessionUpdate(project, Guid.NewGuid(), "Crashed", 1, userId: "maria"));

        var timeline = await _factory.CreateAuthorizedClient().GetFromJsonAsync<JsonElement>($"/users/maria?projectId={project}");

        Assert.Equal(2, timeline.GetProperty("totalEvents").GetInt32());
        Assert.Equal(2, timeline.GetProperty("issues").GetArrayLength());
        Assert.Equal(1, timeline.GetProperty("sessions").GetInt32());
        Assert.Equal(1, timeline.GetProperty("crashedSessions").GetInt32());
        Assert.All(timeline.GetProperty("events").EnumerateArray(), e => Assert.Equal("maria", e.GetProperty("userId").GetString()));
    }
}
