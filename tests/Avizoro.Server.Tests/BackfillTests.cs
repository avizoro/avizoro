using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avizoro.Core.Models;
using Avizoro.Server.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Avizoro.Server.Tests;

public class BackfillTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public BackfillTests(ServerAppFactory factory) => _factory = factory;

    /// <summary>An event as stored by a pre-grouping server: no fingerprint, no issue.</summary>
    private static StoredEvent Legacy(string project, string type, DateTime receivedAt, string release = "1.0") => new()
    {
        Id = Guid.NewGuid(),
        ProjectId = project,
        EventType = "Exception",
        Release = release,
        ExceptionType = type,
        ExceptionJson = JsonSerializer.Serialize(new ExceptionInfo
        {
            Type = type,
            Message = "old",
            StackTrace = "   at MyApp.Legacy.Run() in /src/Legacy.cs:line 3"
        }),
        Timestamp = receivedAt,
        ReceivedAt = receivedAt,
    };

    [Fact]
    public async Task Backfill_GroupsLegacyEvents_AndIsIdempotent()
    {
        const string project = "backfill";
        var t0 = DateTime.UtcNow.AddDays(-3);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AvizoroDbContext>();
            db.Events.AddRange(
                Legacy(project, "System.InvalidOperationException", t0, "1.0"),
                Legacy(project, "System.InvalidOperationException", t0.AddHours(1), "1.1"),
                Legacy(project, "System.TimeoutException", t0.AddHours(2)));
            await db.SaveChangesAsync();
        }

        var authed = _factory.CreateAuthorizedClient();
        var result = await (await authed.PostAsync("/admin/issues/backfill", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, result.GetProperty("eventsGrouped").GetInt32());
        Assert.Equal(2, result.GetProperty("issuesCreated").GetInt32());

        var issues = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        var ioe = issues.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("title").GetString() == "InvalidOperationException");
        Assert.Equal(2, ioe.GetProperty("eventCount").GetInt32());
        Assert.Equal("1.0", ioe.GetProperty("firstRelease").GetString());
        Assert.Equal("1.1", ioe.GetProperty("lastRelease").GetString());
        Assert.Equal("Legacy.Run", ioe.GetProperty("culprit").GetString());

        // A new event of the same problem joins the backfilled issue.
        var live = TestEvents.Exception(projectId: project, exceptionType: "System.InvalidOperationException",
            stackTrace: "   at MyApp.Legacy.Run() in /src/Legacy.cs:line 3");
        await _factory.CreateClient().PostAsJsonAsync("/events", live);

        var again = await (await authed.PostAsync("/admin/issues/backfill", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, again.GetProperty("eventsGrouped").GetInt32());

        var after = await authed.GetFromJsonAsync<JsonElement>($"/issues/{ioe.GetProperty("id").GetGuid()}");
        Assert.Equal(3, after.GetProperty("eventCount").GetInt32());
    }

    [Fact]
    public async Task Backfill_IsAdminOnly()
    {
        var response = await _factory.CreateClient().PostAsync("/admin/issues/backfill", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CustomFingerprint_FromClient_GroupsAcrossStacks()
    {
        const string project = "custom-fp";
        var client = _factory.CreateClient();

        var a = TestEvents.Exception(projectId: project, stackTrace: "   at MyApp.A.One()");
        a["fingerprint"] = new[] { "gateway-timeout" };
        var b = TestEvents.Exception(projectId: project, exceptionType: "System.TimeoutException", stackTrace: "   at MyApp.B.Two()");
        b["fingerprint"] = new[] { "gateway-timeout" };
        await client.PostAsJsonAsync("/events", a);
        await client.PostAsJsonAsync("/events", b);

        var issues = await _factory.CreateAuthorizedClient().GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        Assert.Equal(1, issues.GetProperty("total").GetInt32());
    }
}
