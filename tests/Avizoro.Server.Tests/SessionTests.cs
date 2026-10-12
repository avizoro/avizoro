using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class SessionTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public SessionTests(ServerAppFactory factory) => _factory = factory;

    internal static Dictionary<string, object?> SessionUpdate(
        string project, Guid sessionId, string status, long sequence,
        string release = "1.0.0", string distinctId = "install-a", int errors = 0, string? userId = null)
        => new()
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["projectId"] = project,
            ["release"] = release,
            ["environment"] = "production",
            ["platform"] = "Android",
            ["eventType"] = "Session",
            ["userId"] = userId,
            ["timestamp"] = DateTime.UtcNow,
            ["session"] = new Dictionary<string, object?>
            {
                ["sessionId"] = sessionId.ToString(),
                ["distinctId"] = distinctId,
                ["started"] = DateTime.UtcNow,
                ["status"] = status,
                ["errors"] = errors,
                ["sequence"] = sequence,
                ["durationSeconds"] = 12.5,
            }
        };

    [Fact]
    public async Task SessionEvent_IsAccepted_AndDoesNotCreateEventsOrIssues()
    {
        const string project = "sess-routing";
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/events", SessionUpdate(project, Guid.NewGuid(), "Ok", 1));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var authed = _factory.CreateAuthorizedClient();
        var events = await authed.GetFromJsonAsync<JsonElement>($"/events?projectId={project}");
        var issues = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        Assert.Equal(0, events.GetProperty("total").GetInt32());
        Assert.Equal(0, issues.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task CrashFree_CountsSessionsAndUsers()
    {
        const string project = "sess-rates";
        var client = _factory.CreateClient();

        // User A: one clean session. User B: one clean + one crashed. User C: errored.
        var a = Guid.NewGuid();
        await client.PostAsJsonAsync("/events", SessionUpdate(project, a, "Ok", 1, distinctId: "A"));
        await client.PostAsJsonAsync("/events", SessionUpdate(project, a, "Exited", 2, distinctId: "A"));

        await client.PostAsJsonAsync("/events", SessionUpdate(project, Guid.NewGuid(), "Exited", 1, distinctId: "B"));
        var crashed = Guid.NewGuid();
        await client.PostAsJsonAsync("/events", SessionUpdate(project, crashed, "Ok", 1, distinctId: "B"));
        await client.PostAsJsonAsync("/events", SessionUpdate(project, crashed, "Crashed", 2, distinctId: "B", errors: 1));

        await client.PostAsJsonAsync("/events", SessionUpdate(project, Guid.NewGuid(), "Exited", 1, distinctId: "C", errors: 3));

        var stats = await _factory.CreateAuthorizedClient()
            .GetFromJsonAsync<JsonElement>($"/stats/crash-free?projectId={project}&days=7");

        Assert.Equal(4, stats.GetProperty("sessions").GetInt32());
        Assert.Equal(1, stats.GetProperty("crashedSessions").GetInt32());
        Assert.Equal(1, stats.GetProperty("erroredSessions").GetInt32());
        Assert.Equal(0.75, stats.GetProperty("crashFreeSessionRate").GetDouble());
        Assert.Equal(3, stats.GetProperty("users").GetInt32());
        Assert.Equal(1, stats.GetProperty("crashedUsers").GetInt32());
        Assert.Equal(7, stats.GetProperty("daily").GetArrayLength());
        Assert.Equal(4, stats.GetProperty("daily")[6].GetProperty("sessions").GetInt32());
    }

    [Fact]
    public async Task StaleUpdates_AreIgnored_AndCrashedIsTerminal()
    {
        const string project = "sess-order";
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        await client.PostAsJsonAsync("/events", SessionUpdate(project, id, "Crashed", 3));
        await client.PostAsJsonAsync("/events", SessionUpdate(project, id, "Exited", 2)); // stale
        await client.PostAsJsonAsync("/events", SessionUpdate(project, id, "Ok", 4));     // after crash

        var stats = await _factory.CreateAuthorizedClient()
            .GetFromJsonAsync<JsonElement>($"/stats/crash-free?projectId={project}");

        Assert.Equal(1, stats.GetProperty("sessions").GetInt32());
        Assert.Equal(1, stats.GetProperty("crashedSessions").GetInt32());
    }

    [Fact]
    public async Task NoSessions_GivesNullRates()
    {
        var stats = await _factory.CreateAuthorizedClient()
            .GetFromJsonAsync<JsonElement>("/stats/crash-free?projectId=sess-empty");

        Assert.Equal(0, stats.GetProperty("sessions").GetInt32());
        Assert.Equal(JsonValueKind.Null, stats.GetProperty("crashFreeSessionRate").ValueKind);
    }

    [Fact]
    public async Task SessionEvent_WithoutPayload_IsBadRequest()
    {
        var e = SessionUpdate("sess-bad", Guid.NewGuid(), "Ok", 1);
        e.Remove("session");

        var response = await _factory.CreateClient().PostAsJsonAsync("/events", e);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
