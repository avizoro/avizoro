using System.Net;
using System.Net.Http.Json;

namespace Avizoro.Server.Tests;

public class RateLimitTests
{
    [Fact]
    public async Task Ingest_BeyondTheLimit_Returns429_WithRetryAfter()
    {
        // Permit 3 events per (long) window; the 4th+ must be throttled.
        using var factory = ServerAppFactory.CreateWithIngestLimit(permitLimit: 3, windowSeconds: 60);
        var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? throttled = null;
        for (var i = 0; i < 6; i++)
        {
            var res = await client.PostAsJsonAsync("/events",
                TestEvents.Log(projectId: "rl", id: Guid.NewGuid()));
            statuses.Add(res.StatusCode);
            if (res.StatusCode == HttpStatusCode.TooManyRequests)
                throttled ??= res;
        }

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Created));
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        // A throttled response advertises when to retry.
        Assert.NotNull(throttled);
        Assert.True(throttled!.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task ReadEndpoints_AreNotRateLimited()
    {
        using var factory = ServerAppFactory.CreateWithIngestLimit(permitLimit: 2, windowSeconds: 60);
        var client = factory.CreateAuthorizedClient();

        // Well beyond the ingest permit — reads must never be throttled.
        for (var i = 0; i < 10; i++)
        {
            var res = await client.GetAsync("/events?projectId=none");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        }
    }

    [Fact]
    public async Task Ingest_WhenDisabled_NeverThrottles()
    {
        using var factory = ServerAppFactory.CreateWithIngestLimitDisabled();
        var client = factory.CreateClient();

        for (var i = 0; i < 25; i++)
        {
            var res = await client.PostAsJsonAsync("/events",
                TestEvents.Log(projectId: "rl-off", id: Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }
    }
}
