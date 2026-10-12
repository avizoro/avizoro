using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Avizoro.Server.Integrations;
using Avizoro.Server.Search;
using Microsoft.Extensions.DependencyInjection;

namespace Avizoro.Server.Tests;

public class ExportTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public ExportTests(ServerAppFactory factory) => _factory = factory;

    [Fact]
    public async Task EventsCsv_RespectsFilters_AndEscapes()
    {
        const string project = "export-csv";
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, message: "has, comma and \"quotes\""));
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: project, exceptionType: "System.TimeoutException", message: "=cmd|' /C calc'!A0"));

        var response = await _factory.CreateAuthorizedClient().GetAsync($"/events/export?projectId={project}");
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        var csv = Encoding.UTF8.GetString(await response.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.StartsWith("id,receivedAt,", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.Contains("\"has, comma and \"\"quotes\"\"\"", csv);
        Assert.Contains("'=cmd", csv); // formula injection neutralized

        var filtered = await _factory.CreateAuthorizedClient().GetStringAsync($"/events/export?projectId={project}&q=Timeout");
        Assert.Equal(2, filtered.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public async Task IssuesJson_ExportsSummaries()
    {
        const string project = "export-json";
        await _factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));

        var json = await _factory.CreateAuthorizedClient().GetFromJsonAsync<JsonElement>($"/issues/export?projectId={project}&format=json");

        Assert.Equal("NullReferenceException", json.EnumerateArray().Single().GetProperty("title").GetString());
    }

    [Fact]
    public async Task Export_RequiresReadAuth()
        => Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/events/export")).StatusCode);

    [Fact]
    public void Csv_IsRfc4180()
    {
        var bytes = ExportEndpoints.Csv(["a", "b"], [["x", "line\nbreak"]]);
        Assert.Equal("a,b\r\nx,\"line\nbreak\"\r\n", Encoding.UTF8.GetString(bytes).TrimStart('﻿'));
    }
}

public class TrackerTests : IDisposable
{
    private readonly CapturingHandler _handler = new();
    private readonly ServerAppFactory _factory;

    public TrackerTests()
    {
        _factory = ServerAppFactory.CreateWith(
            services => services.AddHttpClient(IssueTrackerEndpoints.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _handler),
            new Dictionary<string, string?>
            {
                ["Integrations:GitHub:Repository"] = "acme/mobile",
                ["Integrations:GitHub:Token"] = "ghp_test",
                ["Integrations:Jira:BaseUrl"] = "https://acme.atlassian.net",
                ["Integrations:Jira:ProjectKey"] = "MOB",
                ["Alerts:DashboardUrl"] = "https://ik.acme.test",
            });
    }

    public void Dispose() => _factory.Dispose();

    private async Task<Guid> IssueAsync(string project)
    {
        await _factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception(projectId: project));
        return (await _factory.CreateAuthorizedClient().GetFromJsonAsync<JsonElement>($"/issues?projectId={project}"))
            .GetProperty("items")[0].GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task GitHub_CreatesIssueViaApi_LinksIt_AndIsIdempotent()
    {
        _handler.ResponseBody = """{"html_url":"https://github.com/acme/mobile/issues/42"}""";
        _handler.Status = HttpStatusCode.Created;
        var id = await IssueAsync("tracker-gh");
        var authed = _factory.CreateAuthorizedClient();

        var result = await (await authed.PostAsync($"/issues/{id}/external/github", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://github.com/acme/mobile/issues/42", result.GetProperty("url").GetString());
        Assert.True(result.GetProperty("created").GetBoolean());

        var sent = _handler.Requests.Single();
        Assert.Equal("https://api.github.com/repos/acme/mobile/issues", sent.Url.ToString());
        var payload = JsonDocument.Parse(sent.Body).RootElement;
        Assert.Contains("NullReferenceException", payload.GetProperty("title").GetString());
        Assert.Contains("https://ik.acme.test/issues/", payload.GetProperty("body").GetString());
        Assert.Contains("Cart.Checkout", payload.GetProperty("body").GetString()); // stack trace included

        var again = await (await authed.PostAsync($"/issues/{id}/external/github", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(again.GetProperty("created").GetBoolean());
        Assert.Single(_handler.Requests);

        var issue = await authed.GetFromJsonAsync<JsonElement>($"/issues/{id}");
        Assert.Equal("https://github.com/acme/mobile/issues/42", issue.GetProperty("externalIssueUrl").GetString());
    }

    [Fact]
    public async Task Jira_CreatesIssue_AndReturnsBrowseUrl()
    {
        _handler.ResponseBody = """{"key":"MOB-7"}""";
        _handler.Status = HttpStatusCode.Created;
        var id = await IssueAsync("tracker-jira");

        var result = await (await _factory.CreateAuthorizedClient().PostAsync($"/issues/{id}/external/jira", null))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("https://acme.atlassian.net/browse/MOB-7", result.GetProperty("url").GetString());
        Assert.Equal("https://acme.atlassian.net/rest/api/2/issue", _handler.Requests.Single().Url.ToString());
    }

    [Fact]
    public async Task ApiFailure_Is502_AndNothingIsLinked()
    {
        _handler.Status = HttpStatusCode.Unauthorized;
        var id = await IssueAsync("tracker-fail");
        var authed = _factory.CreateAuthorizedClient();

        var response = await authed.PostAsync($"/issues/{id}/external/github", null);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        var issue = await authed.GetFromJsonAsync<JsonElement>($"/issues/{id}");
        Assert.Equal(JsonValueKind.Null, issue.GetProperty("externalIssueUrl").ValueKind);
    }

    [Fact]
    public async Task GitHubWithoutToken_ReturnsPrefilledUrl()
    {
        using var factory = ServerAppFactory.CreateWith(settings: new Dictionary<string, string?>
        {
            ["Integrations:GitHub:Repository"] = "acme/mobile",
        });
        await factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception(projectId: "tracker-prefill"));
        var authed = factory.CreateAuthorizedClient();
        var id = (await authed.GetFromJsonAsync<JsonElement>("/issues?projectId=tracker-prefill")).GetProperty("items")[0].GetProperty("id").GetGuid();

        var info = await authed.GetFromJsonAsync<JsonElement>("/integrations");
        Assert.True(info.GetProperty("gitHub").GetBoolean());
        Assert.False(info.GetProperty("gitHubCreatesViaApi").GetBoolean());

        var result = await (await authed.PostAsync($"/issues/{id}/external/github", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("prefilled").GetBoolean());
        Assert.StartsWith("https://github.com/acme/mobile/issues/new?title=", result.GetProperty("url").GetString());
    }
}
