using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avizoro.Server.Alerts;
using Microsoft.Extensions.DependencyInjection;

namespace Avizoro.Server.Tests;

/// <summary>Captures every outbound alert request instead of hitting the network.</summary>
public sealed class CapturingHandler : HttpMessageHandler
{
    public ConcurrentQueue<(Uri Url, string Body, string? Signature)> Requests { get; } = new();

    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    /// <summary>Optional JSON body returned with every response.</summary>
    public string? ResponseBody { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        request.Headers.TryGetValues(AlertSender.SignatureHeader, out var sig);
        Requests.Enqueue((request.RequestUri!, body, sig?.FirstOrDefault()));
        var response = new HttpResponseMessage(Status);
        if (ResponseBody is not null)
            response.Content = new StringContent(ResponseBody, System.Text.Encoding.UTF8, "application/json");
        return response;
    }
}

public class AlertTests : IDisposable
{
    private readonly CapturingHandler _handler = new();
    private readonly ServerAppFactory _factory;

    public AlertTests()
    {
        _factory = ServerAppFactory.CreateWith(services =>
            services.AddHttpClient(AlertSender.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => _handler));
    }

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task NewIssueRule_PostsSignedWebhook_AndRecordsHistory()
    {
        const string project = "alert-new";
        await CreateRuleAsync(new { name = "new", trigger = "NewIssue", channel = "Webhook", target = "https://hooks.test/new", projectId = project, secret = "s3cret" });

        await PostEventAsync(project);

        var request = await WaitForRequestAsync("https://hooks.test/new");
        var payload = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("NewIssue", payload.GetProperty("trigger").GetString());
        Assert.Equal("NullReferenceException", payload.GetProperty("issue").GetProperty("title").GetString());
        Assert.Equal(AlertSender.Sign(request.Body, "s3cret"), request.Signature);

        var history = await WaitForHistoryAsync(project, n => n.GetProperty("success").ValueKind == JsonValueKind.True);
        Assert.Equal("NewIssue", history.GetProperty("trigger").GetString());
    }

    [Fact]
    public async Task NewIssueRule_DoesNotFire_ForRepeatEvents_OrOtherProjects()
    {
        const string project = "alert-once";
        await CreateRuleAsync(new { name = "new", trigger = "NewIssue", channel = "Webhook", target = "https://hooks.test/once", projectId = project });

        await PostEventAsync(project);
        await PostEventAsync(project);
        await PostEventAsync("some-other-project");
        await WaitForRequestAsync("https://hooks.test/once");
        await Task.Delay(300);

        Assert.Single(_handler.Requests, r => r.Url.ToString() == "https://hooks.test/once");
    }

    [Fact]
    public async Task RegressionRule_FiresWhenResolvedIssueReturns()
    {
        const string project = "alert-regress";
        await CreateRuleAsync(new { name = "regress", trigger = "Regression", channel = "Slack", target = "https://hooks.test/slack", projectId = project });

        await PostEventAsync(project);
        var authed = _factory.CreateAuthorizedClient();
        var issues = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        var id = issues.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await authed.PatchAsJsonAsync($"/issues/{id}", new { status = "Resolved" })).EnsureSuccessStatusCode();

        await PostEventAsync(project);

        var request = await WaitForRequestAsync("https://hooks.test/slack");
        var text = JsonDocument.Parse(request.Body).RootElement.GetProperty("text").GetString();
        Assert.Contains("Regression", text);
    }

    [Fact]
    public async Task ThresholdRule_FiresOnceAtCount_ThenCoolsDown()
    {
        const string project = "alert-threshold";
        await CreateRuleAsync(new
        {
            name = "burst", trigger = "Threshold", channel = "Discord", target = "https://hooks.test/discord",
            projectId = project, thresholdCount = 3, thresholdWindowMinutes = 10, cooldownMinutes = 60
        });

        await PostEventAsync(project);
        await PostEventAsync(project);
        await Task.Delay(200);
        Assert.DoesNotContain(_handler.Requests, r => r.Url.ToString() == "https://hooks.test/discord");

        await PostEventAsync(project);
        await PostEventAsync(project);
        await WaitForRequestAsync("https://hooks.test/discord");
        await Task.Delay(300);

        Assert.Single(_handler.Requests, r => r.Url.ToString() == "https://hooks.test/discord");
    }

    [Fact]
    public async Task IgnoredIssue_NeverAlerts()
    {
        const string project = "alert-ignored";
        await PostEventAsync(project);
        var authed = _factory.CreateAuthorizedClient();
        var issues = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        var id = issues.GetProperty("items")[0].GetProperty("id").GetGuid();
        (await authed.PatchAsJsonAsync($"/issues/{id}", new { status = "Ignored" })).EnsureSuccessStatusCode();

        await CreateRuleAsync(new
        {
            name = "burst", trigger = "Threshold", channel = "Webhook", target = "https://hooks.test/ignored",
            projectId = project, thresholdCount = 1, thresholdWindowMinutes = 10
        });
        await PostEventAsync(project);
        await Task.Delay(300);

        Assert.DoesNotContain(_handler.Requests, r => r.Url.ToString() == "https://hooks.test/ignored");
    }

    [Fact]
    public async Task FailedDelivery_IsRecordedWithError()
    {
        const string project = "alert-fail";
        _handler.Status = HttpStatusCode.InternalServerError;
        await CreateRuleAsync(new { name = "new", trigger = "NewIssue", channel = "Teams", target = "https://hooks.test/teams", projectId = project });

        await PostEventAsync(project);

        var entry = await WaitForHistoryAsync(project, n => n.GetProperty("success").ValueKind == JsonValueKind.False);
        Assert.Contains("500", entry.GetProperty("error").GetString());
    }

    [Fact]
    public async Task TestEndpoint_SendsSyntheticAlert()
    {
        var rule = await CreateRuleAsync(new { name = "t", trigger = "NewIssue", channel = "Webhook", target = "https://hooks.test/test" });

        var response = await _factory.CreateAuthorizedClient()
            .PostAsync($"/alerts/rules/{rule.GetProperty("id").GetGuid()}/test", null);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.Contains(_handler.Requests, r => r.Url.ToString() == "https://hooks.test/test");
    }

    [Theory]
    [InlineData("NewIssue", "Webhook", "not-a-url")]
    [InlineData("Sometimes", "Webhook", "https://x.test")]
    [InlineData("NewIssue", "Pager", "https://x.test")]
    [InlineData("Threshold", "Webhook", "https://x.test")]
    public async Task InvalidRule_IsBadRequest(string trigger, string channel, string target)
    {
        var response = await _factory.CreateAuthorizedClient()
            .PostAsJsonAsync("/alerts/rules", new { name = "bad", trigger, channel, target });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RuleManagement_IsAdminOnly()
    {
        var anonymous = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/alerts/rules")).StatusCode);
    }

    [Fact]
    public async Task UpdateAndDeleteRule()
    {
        var rule = await CreateRuleAsync(new { name = "r", trigger = "NewIssue", channel = "Webhook", target = "https://x.test", secret = "k" });
        var id = rule.GetProperty("id").GetGuid();
        var authed = _factory.CreateAuthorizedClient();

        var put = await authed.PutAsJsonAsync($"/alerts/rules/{id}", new { name = "renamed", trigger = "Regression", channel = "Slack", target = "https://y.test", enabled = false });
        var updated = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("renamed", updated.GetProperty("name").GetString());
        Assert.False(updated.GetProperty("enabled").GetBoolean());
        Assert.True(updated.GetProperty("hasSecret").GetBoolean()); // secret kept when omitted

        Assert.Equal(HttpStatusCode.NoContent, (await authed.DeleteAsync($"/alerts/rules/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await authed.DeleteAsync($"/alerts/rules/{id}")).StatusCode);
    }

    private async Task<JsonElement> CreateRuleAsync(object rule)
    {
        var response = await _factory.CreateAuthorizedClient().PostAsJsonAsync("/alerts/rules", rule);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task PostEventAsync(string project)
        => (await _factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception(projectId: project))).EnsureSuccessStatusCode();

    private async Task<(Uri Url, string Body, string? Signature)> WaitForRequestAsync(string url)
    {
        for (var i = 0; i < 100; i++)
        {
            var hit = _handler.Requests.FirstOrDefault(r => r.Url.ToString() == url);
            if (hit.Url is not null)
                return hit;
            await Task.Delay(50);
        }
        throw new TimeoutException($"No alert was sent to {url}.");
    }

    private async Task<JsonElement> WaitForHistoryAsync(string project, Func<JsonElement, bool> predicate)
    {
        var authed = _factory.CreateAuthorizedClient();
        for (var i = 0; i < 100; i++)
        {
            var page = await authed.GetFromJsonAsync<JsonElement>($"/alerts/history?projectId={project}");
            foreach (var item in page.GetProperty("items").EnumerateArray())
                if (predicate(item))
                    return item;
            await Task.Delay(50);
        }
        throw new TimeoutException("Alert history never reached the expected state.");
    }
}
