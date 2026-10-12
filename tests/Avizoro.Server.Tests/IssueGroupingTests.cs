using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class IssueGroupingTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public IssueGroupingTests(ServerAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RepeatedException_CollapsesIntoOneIssue_WithBumpedCount()
    {
        var client = _factory.CreateClient();
        const string project = "grouping-same";

        // Same exception type + top frame, different event ids → one issue.
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, message: "first"));
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, message: "second"));
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, message: "third"));

        var issues = await Issues(project);

        Assert.Equal(1, issues.GetProperty("total").GetInt32());
        var issue = issues.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(3, issue.GetProperty("eventCount").GetInt32());
        Assert.Equal("NullReferenceException", issue.GetProperty("title").GetString());
        Assert.Equal("Cart.Checkout", issue.GetProperty("culprit").GetString());
    }

    [Fact]
    public async Task DifferentExceptionTypes_ProduceSeparateIssues()
    {
        var client = _factory.CreateClient();
        const string project = "grouping-diff";

        await client.PostAsJsonAsync("/events",
            TestEvents.Exception(projectId: project, exceptionType: "System.ArgumentException"));
        await client.PostAsJsonAsync("/events",
            TestEvents.Exception(projectId: project, exceptionType: "System.InvalidOperationException"));

        var issues = await Issues(project);

        Assert.Equal(2, issues.GetProperty("total").GetInt32());
        foreach (var issue in issues.GetProperty("items").EnumerateArray())
            Assert.Equal(1, issue.GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public async Task IssueDetail_ById_ReturnsGroupedCount()
    {
        var client = _factory.CreateClient();
        const string project = "grouping-detail";

        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));

        var issues = await Issues(project);
        var issueId = issues.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var authed = _factory.CreateAuthorizedClient();
        var detail = await authed.GetFromJsonAsync<JsonElement>($"/issues/{issueId}");

        Assert.Equal(issueId, detail.GetProperty("id").GetGuid());
        Assert.Equal(2, detail.GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public async Task IssueEvents_ListsEveryOccurrence()
    {
        var client = _factory.CreateClient();
        const string project = "grouping-events";

        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));

        var issues = await Issues(project);
        var issueId = issues.GetProperty("items").EnumerateArray().Single().GetProperty("id").GetGuid();

        var authed = _factory.CreateAuthorizedClient();
        var events = await authed.GetFromJsonAsync<JsonElement>($"/issues/{issueId}/events");

        Assert.Equal(2, events.GetProperty("total").GetInt32());
    }

    private async Task<JsonElement> Issues(string projectId)
    {
        var client = _factory.CreateAuthorizedClient();
        return await client.GetFromJsonAsync<JsonElement>($"/issues?projectId={projectId}");
    }
}
