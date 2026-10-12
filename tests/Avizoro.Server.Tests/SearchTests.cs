using System.Net.Http.Json;
using System.Text.Json;

namespace Avizoro.Server.Tests;

public class SearchTests : IClassFixture<ServerAppFactory>
{
    private const string Project = "search";
    private readonly ServerAppFactory _factory;
    private static readonly SemaphoreSlim Seeded = new(1, 1);
    private static bool _seeded;

    public SearchTests(ServerAppFactory factory) => _factory = factory;

    private async Task SeedAsync()
    {
        await Seeded.WaitAsync();
        try
        {
            if (_seeded)
                return;

            var client = _factory.CreateClient();
            async Task Post(Action<Dictionary<string, object?>> tweak, string type = "System.NullReferenceException", string message = "boom")
            {
                var e = TestEvents.Exception(projectId: Project, exceptionType: type, message: message);
                tweak(e);
                (await client.PostAsJsonAsync("/events", e)).EnsureSuccessStatusCode();
            }

            await Post(e => { e["release"] = "2.0"; e["userId"] = "u-1"; e["tags"] = new Dictionary<string, string> { ["tenant"] = "acme", ["plan"] = "pro" }; }, message: "payment gateway timeout");
            await Post(e => { e["release"] = "2.1"; e["platform"] = "iOS"; e["operatingSystem"] = "iOS 18.2"; e["tags"] = new Dictionary<string, string> { ["tenant"] = "globex" }; });
            await Post(e => { e["environment"] = "production"; e["deviceModel"] = "Pixel 9 Pro"; }, type: "System.TimeoutException");
            _seeded = true;
        }
        finally
        {
            Seeded.Release();
        }
    }

    private async Task<int> CountAsync(string query)
    {
        await SeedAsync();
        var page = await _factory.CreateAuthorizedClient().GetFromJsonAsync<JsonElement>($"/events?projectId={Project}&{query}");
        return page.GetProperty("total").GetInt32();
    }

    [Theory]
    [InlineData("q=gateway", 1)]
    [InlineData("q=TimeoutException", 1)]
    [InlineData("release=2.0", 1)]
    [InlineData("userId=u-1", 1)]
    [InlineData("platform=iOS", 1)]
    [InlineData("environment=production", 1)]
    [InlineData("operatingSystem=18", 1)]
    [InlineData("deviceModel=Pixel", 1)]
    [InlineData("tag=tenant:acme", 1)]
    [InlineData("tag=tenant:acme&tag=plan:pro", 1)]
    [InlineData("tag=tenant:acme&tag=plan:free", 0)]
    [InlineData("tag=tenant:ac", 0)] // exact value, not prefix
    [InlineData("level=Error", 3)]
    [InlineData("q=nothing-matches", 0)]
    public async Task EventFilters_Narrow(string query, int expected)
        => Assert.Equal(expected, await CountAsync(query));

    [Fact]
    public async Task DateRange_Filters()
    {
        Assert.Equal(3, await CountAsync($"from={Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"))}"));
        Assert.Equal(0, await CountAsync($"to={Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"))}"));
    }

    [Fact]
    public async Task IssueSearch_ByTitleAndCulprit()
    {
        await SeedAsync();
        var authed = _factory.CreateAuthorizedClient();

        var byTitle = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={Project}&q=Timeout");
        Assert.Equal(1, byTitle.GetProperty("total").GetInt32());

        var byCulprit = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={Project}&q=Cart.Checkout");
        Assert.Equal(2, byCulprit.GetProperty("total").GetInt32());
    }
}
