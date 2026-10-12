using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class ProjectScopingTests
{
    // Each test gets its own in-memory DB so global counts (e.g. the admin's total)
    // aren't perturbed by sibling tests sharing a fixture.
    private static async Task SeedEvent(HttpClient client, string projectId, Guid id)
    {
        var res = await client.PostAsJsonAsync("/events",
            TestEvents.Exception(projectId: projectId, id: id));
        res.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task ProjectKey_SeesOnlyItsOwnEventsAndIssues_AdminSeesAll()
    {
        using var _factory = new ServerAppFactory();
        var admin = _factory.CreateAuthorizedClient();
        var a = await ProjectApi.CreateAsync(admin, "scope-a");
        var b = await ProjectApi.CreateAsync(admin, "scope-b");

        var ingest = _factory.CreateClient();
        await SeedEvent(ingest, "scope-a", Guid.NewGuid());
        await SeedEvent(ingest, "scope-a", Guid.NewGuid());
        await SeedEvent(ingest, "scope-b", Guid.NewGuid());

        var readerA = _factory.CreateClientWithToken(a.ReadKey);

        // Events: reader A sees only its two.
        var eventsA = await readerA.GetFromJsonAsync<JsonElement>("/events");
        Assert.Equal(2, eventsA.GetProperty("total").GetInt32());
        Assert.All(eventsA.GetProperty("items").EnumerateArray(),
            e => Assert.Equal("scope-a", e.GetProperty("projectId").GetString()));

        // Issues: only project A's issue.
        var issuesA = await readerA.GetFromJsonAsync<JsonElement>("/issues");
        Assert.All(issuesA.GetProperty("items").EnumerateArray(),
            i => Assert.Equal("scope-a", i.GetProperty("projectId").GetString()));

        // Admin sees both projects' events.
        var eventsAdmin = await admin.GetFromJsonAsync<JsonElement>("/events");
        Assert.Equal(3, eventsAdmin.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ProjectKey_CannotWidenScopeViaQueryParam()
    {
        using var _factory = new ServerAppFactory();
        var admin = _factory.CreateAuthorizedClient();
        var a = await ProjectApi.CreateAsync(admin, "widen-a");
        await ProjectApi.CreateAsync(admin, "widen-b");

        var ingest = _factory.CreateClient();
        await SeedEvent(ingest, "widen-a", Guid.NewGuid());
        await SeedEvent(ingest, "widen-b", Guid.NewGuid());

        var readerA = _factory.CreateClientWithToken(a.ReadKey);

        // Asking for project B is ignored — still only A's data comes back.
        var events = await readerA.GetFromJsonAsync<JsonElement>("/events?projectId=widen-b");
        Assert.Equal(1, events.GetProperty("total").GetInt32());
        Assert.All(events.GetProperty("items").EnumerateArray(),
            e => Assert.Equal("widen-a", e.GetProperty("projectId").GetString()));
    }

    [Fact]
    public async Task ProjectKey_GettingAnotherProjectsEventById_Is404()
    {
        using var _factory = new ServerAppFactory();
        var admin = _factory.CreateAuthorizedClient();
        var a = await ProjectApi.CreateAsync(admin, "x-a");
        await ProjectApi.CreateAsync(admin, "x-b");

        var ingest = _factory.CreateClient();
        var bEventId = Guid.NewGuid();
        await SeedEvent(ingest, "x-b", bEventId);

        var readerA = _factory.CreateClientWithToken(a.ReadKey);

        // Admin can fetch it; the scoped A key cannot (404, not 403 — no existence leak).
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/events/{bEventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await readerA.GetAsync($"/events/{bEventId}")).StatusCode);
    }
}
