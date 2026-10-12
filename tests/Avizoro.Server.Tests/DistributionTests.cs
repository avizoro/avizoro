using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avizoro.Server.Search;

namespace Avizoro.Server.Tests;

public class DistributionTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public DistributionTests(ServerAppFactory factory) => _factory = factory;

    [Fact]
    public async Task IssueDistributions_ShowSharesPerDimensionAndTag()
    {
        const string project = "dist";
        var client = _factory.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            var e = TestEvents.Exception(projectId: project);
            e["operatingSystem"] = i < 3 ? "Android 14" : "Android 13";
            e["release"] = "2.3.1";
            e["userId"] = $"u{i % 2}";
            e["tags"] = new Dictionary<string, string> { ["tenant"] = i == 0 ? "globex" : "acme" };
            await client.PostAsJsonAsync("/events", e);
        }

        var authed = _factory.CreateAuthorizedClient();
        var issueId = (await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}")).GetProperty("items")[0].GetProperty("id").GetGuid();
        var result = await authed.GetFromJsonAsync<JsonElement>($"/issues/{issueId}/distributions");

        Assert.Equal(4, result.GetProperty("sampledEvents").GetInt32());
        Assert.Equal(2, result.GetProperty("affectedUsers").GetInt32());

        var byKey = result.GetProperty("distributions").EnumerateArray().ToDictionary(d => d.GetProperty("key").GetString()!);
        var os = byKey["os"].GetProperty("values")[0];
        Assert.Equal("Android 14", os.GetProperty("value").GetString());
        Assert.Equal(0.75, os.GetProperty("share").GetDouble());
        Assert.Equal(1.0, byKey["release"].GetProperty("values")[0].GetProperty("share").GetDouble());
        Assert.Equal("acme", byKey["tag:tenant"].GetProperty("values")[0].GetProperty("value").GetString());
        Assert.False(byKey.ContainsKey("manufacturer")); // blank everywhere → omitted
    }

    [Fact]
    public async Task UnknownIssue_Is404()
    {
        var response = await _factory.CreateAuthorizedClient().GetAsync($"/issues/{Guid.NewGuid()}/distributions");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void Distribute_CollapsesTheLongTail()
    {
        var values = Enumerable.Range(0, 8).Select(i => $"v{i}").Concat(["v0", "v0"]);
        var d = DistributionEndpoints.Distribute("k", values);

        Assert.Equal(10, d.Total);
        Assert.Equal(6, d.Values.Count); // top 5 + (other)
        Assert.Equal(("v0", 3), (d.Values[0].Value, d.Values[0].Count));
        Assert.Equal(("(other)", 3), (d.Values[^1].Value, d.Values[^1].Count));
    }
}
