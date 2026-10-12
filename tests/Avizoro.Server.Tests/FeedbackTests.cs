using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class FeedbackTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public FeedbackTests(ServerAppFactory factory) => _factory = factory;

    private static Dictionary<string, object?> FeedbackEvent(string project, Guid eventId, string comments, string? email = null)
        => new()
        {
            ["id"] = Guid.NewGuid().ToString(),
            ["projectId"] = project,
            ["eventType"] = "Feedback",
            ["timestamp"] = DateTime.UtcNow,
            ["feedback"] = new Dictionary<string, object?>
            {
                ["eventId"] = eventId.ToString(),
                ["comments"] = comments,
                ["email"] = email,
            }
        };

    [Fact]
    public async Task Feedback_IsLinkedToTheCrashIssue_AndListed()
    {
        const string project = "feedback-link";
        var client = _factory.CreateClient();
        var crashId = Guid.NewGuid();
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, id: crashId));

        var response = await client.PostAsJsonAsync("/events", FeedbackEvent(project, crashId, "I tapped Pay and it closed", "ana@example.com"));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var authed = _factory.CreateAuthorizedClient();
        var issue = (await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}")).GetProperty("items")[0];

        var forIssue = await authed.GetFromJsonAsync<JsonElement>($"/issues/{issue.GetProperty("id").GetGuid()}/feedback");
        var entry = forIssue.EnumerateArray().Single();
        Assert.Equal("I tapped Pay and it closed", entry.GetProperty("comments").GetString());
        Assert.Equal("ana@example.com", entry.GetProperty("email").GetString()); // user-provided, not scrubbed

        var forEvent = await authed.GetFromJsonAsync<JsonElement>($"/events/{crashId}/feedback");
        Assert.Single(forEvent.EnumerateArray());

        var all = await authed.GetFromJsonAsync<JsonElement>($"/feedback?projectId={project}");
        Assert.Equal(1, all.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task FeedbackArrivingBeforeItsEvent_StillShowsOnTheIssue()
    {
        const string project = "feedback-early";
        var client = _factory.CreateClient();
        var crashId = Guid.NewGuid();

        await client.PostAsJsonAsync("/events", FeedbackEvent(project, crashId, "it froze"));
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, id: crashId));

        var authed = _factory.CreateAuthorizedClient();
        var issue = (await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}")).GetProperty("items")[0];
        var forIssue = await authed.GetFromJsonAsync<JsonElement>($"/issues/{issue.GetProperty("id").GetGuid()}/feedback");

        Assert.Equal("it froze", forIssue.EnumerateArray().Single().GetProperty("comments").GetString());
    }

    [Fact]
    public async Task EmptyFeedback_IsRejected_AndDoesNotCreateIssues()
    {
        const string project = "feedback-bad";
        var response = await _factory.CreateClient().PostAsJsonAsync("/events", FeedbackEvent(project, Guid.NewGuid(), "  "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var issues = await _factory.CreateAuthorizedClient().GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        Assert.Equal(0, issues.GetProperty("total").GetInt32());
    }
}
