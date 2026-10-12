using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class IngestTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public IngestTests(ServerAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PostEvent_IsOpen_NoAuthRequired()
    {
        var client = _factory.CreateClient(); // no token header
        var res = await client.PostAsJsonAsync("/events", TestEvents.Exception());

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task PostEvent_SameId_IsIdempotent()
    {
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        var first = await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: "idem", id: id));
        var second = await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: "idem", id: id));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        // A re-send of an already-stored event is treated as success, not a conflict.
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        // And only one row exists for that project.
        var total = await CountEvents("idem");
        Assert.Equal(1, total);
    }

    [Fact]
    public async Task PostEvent_EventTypeAsString_IsIngested()
    {
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        var res = await client.PostAsJsonAsync("/events",
            TestEvents.Log(projectId: "enum-str", eventType: "Log", id: id));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        Assert.Equal("Log", await EventTypeOf(id, "enum-str"));
    }

    [Fact]
    public async Task PostEvent_EventTypeAsNumber_IsIngested()
    {
        // The known ingest gotcha: a hand-rolled client posting eventType as a raw
        // number must still work (JsonStringEnumConverter reads numbers too).
        // EventType.Exception == 0.
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        var res = await client.PostAsJsonAsync("/events",
            TestEvents.Exception(projectId: "enum-num", id: id, eventType: 0));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        Assert.Equal("Exception", await EventTypeOf(id, "enum-num"));
    }

    [Fact]
    public async Task PostEvent_ThenGetDetail_RoundTripsExceptionTree()
    {
        var client = _factory.CreateAuthorizedClient();
        var id = Guid.NewGuid();

        await client.PostAsJsonAsync("/events",
            TestEvents.Exception(projectId: "detail", id: id, message: "detail-boom"));

        var detail = await client.GetFromJsonAsync<JsonElement>($"/events/{id}");

        Assert.Equal("Exception", detail.GetProperty("eventType").GetString());
        Assert.Equal("detail-boom", detail.GetProperty("exception").GetProperty("message").GetString());
        Assert.False(detail.GetProperty("hasScreenshot").GetBoolean());
    }

    private async Task<int> CountEvents(string projectId)
    {
        var client = _factory.CreateAuthorizedClient();
        var page = await client.GetFromJsonAsync<JsonElement>($"/events?projectId={projectId}");
        return page.GetProperty("total").GetInt32();
    }

    private async Task<string?> EventTypeOf(Guid id, string projectId)
    {
        var client = _factory.CreateAuthorizedClient();
        var page = await client.GetFromJsonAsync<JsonElement>($"/events?projectId={projectId}");
        foreach (var item in page.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("id").GetGuid() == id)
                return item.GetProperty("eventType").GetString();
        }
        return null;
    }
}
