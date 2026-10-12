using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Avizoro.Server.Tests;

public class AuthTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory; // token configured, Development

    public AuthTests(ServerAppFactory factory) => _factory = factory;

    [Fact]
    public async Task ReadEndpoint_WithoutToken_Is401()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/events");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoint_WithCorrectToken_Is200()
    {
        var client = _factory.CreateAuthorizedClient();
        var res = await client.GetAsync("/events");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoint_WithWrongToken_Is401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-token");
        var res = await client.GetAsync("/events");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoint_TokenViaQueryString_Is200()
    {
        // WebSockets/<img> can't set a header, so a query access_token is accepted.
        var client = _factory.CreateClient();
        var res = await client.GetAsync($"/events?access_token={ServerAppFactory.TestToken}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Hub_Negotiate_WithoutToken_Is401()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Hub_Negotiate_WithToken_Succeeds()
    {
        var client = _factory.CreateAuthorizedClient();
        var res = await client.PostAsync("/hubs/events/negotiate?negotiateVersion=1", content: null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Ingest_StaysOpen_EvenWhenReadTokenConfigured()
    {
        var client = _factory.CreateClient(); // no token
        var res = await client.PostAsJsonAsync("/events", TestEvents.Log(projectId: "auth-ingest"));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
    }

    [Fact]
    public async Task NoToken_InProduction_FailsClosed()
    {
        using var prod = ServerAppFactory.Create(environment: "Production", withToken: false);
        var client = prod.CreateClient();
        var res = await client.GetAsync("/events");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task NoToken_InDevelopment_IsOpen()
    {
        using var dev = ServerAppFactory.Create(environment: "Development", withToken: false);
        var client = dev.CreateClient();
        var res = await client.GetAsync("/events");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}
