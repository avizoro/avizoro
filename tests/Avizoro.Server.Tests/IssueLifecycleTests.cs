using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avizoro.Server.Ingest;

namespace Avizoro.Server.Tests;

public class IssueLifecycleTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public IssueLifecycleTests(ServerAppFactory factory) => _factory = factory;

    [Fact]
    public async Task NewIssue_StartsUnresolved()
    {
        var issue = await CreateIssueAsync("life-new");

        Assert.Equal("Unresolved", issue.GetProperty("status").GetString());
        Assert.False(issue.GetProperty("isRegression").GetBoolean());
    }

    [Fact]
    public async Task Resolve_ThenNewEvent_ReopensAsRegression()
    {
        const string project = "life-regress";
        var issue = await CreateIssueAsync(project);
        var id = issue.GetProperty("id").GetGuid();

        var resolved = await PatchAsync(id, new { status = "resolved" });
        Assert.Equal("Resolved", resolved.GetProperty("status").GetString());
        Assert.True(resolved.TryGetProperty("resolvedAt", out var at) && at.ValueKind != JsonValueKind.Null);

        await _factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));

        var after = await GetIssueAsync(id);
        Assert.Equal("Unresolved", after.GetProperty("status").GetString());
        Assert.True(after.GetProperty("isRegression").GetBoolean());
        Assert.Equal(2, after.GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public async Task ResolvedInRelease_OlderReleaseDoesNotReopen_NewerDoes()
    {
        const string project = "life-release";
        var issue = await CreateIssueAsync(project);
        var id = issue.GetProperty("id").GetGuid();

        await PatchAsync(id, new { status = "Resolved", resolvedInRelease = "2.0.0" });

        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/events", WithRelease(TestEvents.Exception(projectId: project), "1.9.0"));
        Assert.Equal("Resolved", (await GetIssueAsync(id)).GetProperty("status").GetString());

        await client.PostAsJsonAsync("/events", WithRelease(TestEvents.Exception(projectId: project), "2.0.1"));
        var after = await GetIssueAsync(id);
        Assert.Equal("Unresolved", after.GetProperty("status").GetString());
        Assert.True(after.GetProperty("isRegression").GetBoolean());
    }

    [Fact]
    public async Task IgnoredIssue_StaysIgnored_WhenEventsArrive()
    {
        const string project = "life-ignore";
        var issue = await CreateIssueAsync(project);
        var id = issue.GetProperty("id").GetGuid();

        await PatchAsync(id, new { status = "Ignored" });
        await _factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));

        var after = await GetIssueAsync(id);
        Assert.Equal("Ignored", after.GetProperty("status").GetString());
        Assert.False(after.GetProperty("isRegression").GetBoolean());
    }

    [Fact]
    public async Task Assign_And_Clear()
    {
        var issue = await CreateIssueAsync("life-assign");
        var id = issue.GetProperty("id").GetGuid();

        var assigned = await PatchAsync(id, new { assignedTo = "ana@example.com" });
        Assert.Equal("ana@example.com", assigned.GetProperty("assignedTo").GetString());
        Assert.Equal("Unresolved", assigned.GetProperty("status").GetString());

        var cleared = await PatchAsync(id, new { assignedTo = "" });
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("assignedTo").ValueKind);
    }

    [Fact]
    public async Task StatusFilter_ListsOnlyMatchingIssues()
    {
        const string project = "life-filter";
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, exceptionType: "System.A"));
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, exceptionType: "System.B"));

        var authed = _factory.CreateAuthorizedClient();
        var all = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        var first = all.GetProperty("items").EnumerateArray().First().GetProperty("id").GetGuid();
        await PatchAsync(first, new { status = "Resolved" });

        var resolved = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}&status=resolved");
        var unresolved = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}&status=Unresolved");

        Assert.Equal(1, resolved.GetProperty("total").GetInt32());
        Assert.Equal(1, unresolved.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task UnknownStatus_IsBadRequest()
    {
        var issue = await CreateIssueAsync("life-bad");
        var authed = _factory.CreateAuthorizedClient();

        var response = await authed.PatchAsJsonAsync($"/issues/{issue.GetProperty("id").GetGuid()}", new { status = "closed" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var list = await authed.GetAsync("/issues?status=closed");
        Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);
    }

    [Fact]
    public async Task Patch_RequiresReadAuth()
    {
        var issue = await CreateIssueAsync("life-auth");
        var anonymous = _factory.CreateClient();

        var response = await anonymous.PatchAsJsonAsync($"/issues/{issue.GetProperty("id").GetGuid()}", new { status = "Resolved" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null, "1.0.0", true)]
    [InlineData("2.0.0", "1.9.9", false)]
    [InlineData("2.0.0", "2.0.0", true)]
    [InlineData("v2.0", "2.1.0-beta", true)]
    [InlineData("2.0.0", "nightly", true)]
    [InlineData("build-7", "build-6", true)]
    public void Regresses_ComparesVersionsConservatively(string? resolvedIn, string? eventRelease, bool expected)
        => Assert.Equal(expected, EventIngestor.Regresses(resolvedIn, eventRelease));

    private static Dictionary<string, object?> WithRelease(Dictionary<string, object?> e, string release)
    {
        e["release"] = release;
        return e;
    }

    private async Task<JsonElement> CreateIssueAsync(string project)
    {
        await _factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));
        var list = await _factory.CreateAuthorizedClient()
            .GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        return list.GetProperty("items").EnumerateArray().Single();
    }

    private async Task<JsonElement> GetIssueAsync(Guid id)
        => await _factory.CreateAuthorizedClient().GetFromJsonAsync<JsonElement>($"/issues/{id}");

    private async Task<JsonElement> PatchAsync(Guid id, object body)
    {
        var response = await _factory.CreateAuthorizedClient().PatchAsJsonAsync($"/issues/{id}", body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
