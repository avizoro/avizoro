using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class ScreenshotTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public ScreenshotTests(ServerAppFactory factory) => _factory = factory;

    private static ByteArrayContent Jpeg(byte[] bytes)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return content;
    }

    [Fact]
    public async Task UploadThenFetch_RoundTripsBytes_AndMarksDetail()
    {
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: "shot", id: id));

        var payload = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
        var upload = await client.PostAsync($"/events/{id}/screenshot", Jpeg(payload));
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var authed = _factory.CreateAuthorizedClient();

        var img = await authed.GetAsync($"/events/{id}/screenshot");
        Assert.Equal(HttpStatusCode.OK, img.StatusCode);
        Assert.Equal("image/jpeg", img.Content.Headers.ContentType?.MediaType);
        Assert.Equal(payload, await img.Content.ReadAsByteArrayAsync());

        var detail = await authed.GetFromJsonAsync<JsonElement>($"/events/{id}");
        Assert.True(detail.GetProperty("hasScreenshot").GetBoolean());
    }

    [Fact]
    public async Task Upload_OversizedScreenshot_Is400()
    {
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();
        await client.PostAsJsonAsync("/events", TestEvents.Exception(projectId: "shot-big", id: id));

        var tooBig = new byte[2 * 1024 * 1024 + 1]; // just over the 2 MB cap
        var res = await client.PostAsync($"/events/{id}/screenshot", Jpeg(tooBig));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task FetchScreenshot_WithoutToken_Is401()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync($"/events/{Guid.NewGuid()}/screenshot");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task FetchScreenshot_Missing_Is404()
    {
        var authed = _factory.CreateAuthorizedClient();
        var res = await authed.GetAsync($"/events/{Guid.NewGuid()}/screenshot");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
