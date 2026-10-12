using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IntelligenceKit.Server.Contracts;
using IntelligenceKit.Server.Data;
using IntelligenceKit.Server.Retention;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IntelligenceKit.Server.Tests;

public class SessionTimelineTests : IClassFixture<ServerAppFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private readonly ServerAppFactory _factory;

    public SessionTimelineTests(ServerAppFactory factory) => _factory = factory;

    internal static Dictionary<string, object?> TimelineBatch(
        string project, Guid sessionId, long sequence, IEnumerable<(string Category, string Message, DateTime At)> entries, Guid? id = null)
        => new()
        {
            ["id"] = (id ?? Guid.NewGuid()).ToString(),
            ["projectId"] = project,
            ["release"] = "1.0.0",
            ["platform"] = "Android",
            ["eventType"] = "Timeline",
            ["sessionId"] = sessionId.ToString(),
            ["timelineSequence"] = sequence,
            ["timestamp"] = DateTime.UtcNow,
            ["breadcrumbs"] = entries.Select(e => new Dictionary<string, object?>
            {
                ["timestamp"] = e.At,
                ["category"] = e.Category,
                ["message"] = e.Message,
                ["level"] = "Information",
                ["data"] = new Dictionary<string, string> { ["screen"] = "HomePage" },
            }).ToList(),
        };

    private static Dictionary<string, object?> SessionStart(string project, Guid sessionId)
    {
        var update = SessionTests.SessionUpdate(project, sessionId, "Ok", 1);
        update["deviceModel"] = "Pixel 8";
        update["manufacturer"] = "Google";
        update["operatingSystem"] = "Android 15";
        return update;
    }

    private async Task<SessionTimelineResult> TimelineAsync(HttpClient client, Guid sessionId)
        => (await client.GetFromJsonAsync<SessionTimelineResult>($"/sessions/{sessionId}/timeline", Json))!;

    [Fact]
    public async Task TimelineBatch_IsStoredPerSession_AndMergedWithTheSessionsErrors()
    {
        const string project = "tl-merge";
        var sessionId = Guid.NewGuid();
        var t0 = DateTime.UtcNow.AddMinutes(-1);
        var ingest = _factory.CreateClient();

        await ingest.PostAsJsonAsync("/events", SessionStart(project, sessionId));
        var batch = await ingest.PostAsJsonAsync("/events", TimelineBatch(project, sessionId, 0,
        [
            ("navigation", "HomePage", t0),
            ("user", "transfer_clicked", t0.AddSeconds(2)),
            ("http", "POST api.example.com/transfer", t0.AddSeconds(3)),
        ]));
        Assert.Equal(HttpStatusCode.Accepted, batch.StatusCode);

        var error = TestEvents.Exception(project);
        error["sessionId"] = sessionId.ToString();
        error["timestamp"] = t0.AddSeconds(4);
        (await ingest.PostAsJsonAsync("/events", error)).EnsureSuccessStatusCode();

        var timeline = await TimelineAsync(_factory.CreateAuthorizedClient(), sessionId);

        Assert.Equal(["HomePage", "transfer_clicked", "POST api.example.com/transfer", "System.NullReferenceException"],
            timeline.Items.Select(i => i.Message));
        var errorItem = timeline.Items[^1];
        Assert.Equal(TimelineItem.EventKind, errorItem.Kind);
        Assert.Equal(Guid.Parse((string)error["id"]!), errorItem.EventId);
        Assert.Equal("HomePage", timeline.Items[0].Data!["screen"]);

        Assert.Equal(3, timeline.Session.TimelineEntries);
        Assert.Equal("Pixel 8", timeline.Session.DeviceModel);
        Assert.Equal("Android 15", timeline.Session.OperatingSystem);
    }

    [Fact]
    public async Task LogBreadcrumb_AndItsLogEvent_AppearOnce()
    {
        const string project = "tl-dedup";
        var sessionId = Guid.NewGuid();
        var at = DateTime.UtcNow.AddMinutes(-1);
        var ingest = _factory.CreateClient();
        await ingest.PostAsJsonAsync("/events", SessionStart(project, sessionId));

        // TrackLogAsync records both a "log" breadcrumb and a Log event for the same call.
        await ingest.PostAsJsonAsync("/events", TimelineBatch(project, sessionId, 0,
            [("log", "Payment token empty", at), ("log", "Unrelated log line", at.AddSeconds(1))]));
        var log = TestEvents.Log(project, "Payment token empty");
        log["sessionId"] = sessionId.ToString();
        log["timestamp"] = at.AddMilliseconds(3);
        await ingest.PostAsJsonAsync("/events", log);

        var items = (await TimelineAsync(_factory.CreateAuthorizedClient(), sessionId)).Items;

        var payment = Assert.Single(items, i => i.Message == "Payment token empty");
        Assert.Equal(TimelineItem.EventKind, payment.Kind);
        Assert.Contains(items, i => i.Message == "Unrelated log line");
    }

    [Fact]
    public async Task TimelineBatch_CreatesNoEventsOrIssues_AndIsIdempotent()
    {
        const string project = "tl-idem";
        var sessionId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var ingest = _factory.CreateClient();
        await ingest.PostAsJsonAsync("/events", SessionStart(project, sessionId));

        var body = TimelineBatch(project, sessionId, 0, [("navigation", "HomePage", DateTime.UtcNow)], batchId);
        await ingest.PostAsJsonAsync("/events", body);
        await ingest.PostAsJsonAsync("/events", body); // SDK retry

        var authed = _factory.CreateAuthorizedClient();
        Assert.Single((await TimelineAsync(authed, sessionId)).Items);
        var events = await authed.GetFromJsonAsync<JsonElement>($"/events?projectId={project}");
        var issues = await authed.GetFromJsonAsync<JsonElement>($"/issues?projectId={project}");
        Assert.Equal(0, events.GetProperty("total").GetInt32());
        Assert.Equal(0, issues.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task TimelineBatch_WithoutSessionOrEntries_IsRejected()
    {
        var ingest = _factory.CreateClient();

        var noSession = TimelineBatch("tl-bad", Guid.NewGuid(), 0, [("navigation", "HomePage", DateTime.UtcNow)]);
        noSession.Remove("sessionId");
        var empty = TimelineBatch("tl-bad", Guid.NewGuid(), 0, []);

        Assert.Equal(HttpStatusCode.BadRequest, (await ingest.PostAsJsonAsync("/events", noSession)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ingest.PostAsJsonAsync("/events", empty)).StatusCode);
    }

    [Fact]
    public async Task TimelineBatch_IsCappedPerBatch()
    {
        const string project = "tl-cap";
        var sessionId = Guid.NewGuid();
        var ingest = _factory.CreateClient();
        await ingest.PostAsJsonAsync("/events", SessionStart(project, sessionId));

        var entries = Enumerable.Range(0, TimelineLimits.EntriesPerBatch + 50)
            .Select(i => ("user", $"tap {i}", DateTime.UtcNow.AddMilliseconds(i)));
        await ingest.PostAsJsonAsync("/events", TimelineBatch(project, sessionId, 0, entries));

        Assert.Equal(TimelineLimits.EntriesPerBatch, (await TimelineAsync(_factory.CreateAuthorizedClient(), sessionId)).Items.Count);
    }

    [Fact]
    public async Task Sessions_AreListedNewestFirst_WithFilters()
    {
        const string project = "tl-list";
        var ingest = _factory.CreateClient();
        var older = Guid.NewGuid();
        var newer = Guid.NewGuid();

        var olderStart = SessionStart(project, older);
        ((Dictionary<string, object?>)olderStart["session"]!)["started"] = DateTime.UtcNow.AddHours(-2);
        await ingest.PostAsJsonAsync("/events", olderStart);
        await ingest.PostAsJsonAsync("/events", SessionStart(project, newer));
        await ingest.PostAsJsonAsync("/events", TimelineBatch(project, newer, 0, [("navigation", "HomePage", DateTime.UtcNow)]));
        await ingest.PostAsJsonAsync("/events", SessionTests.SessionUpdate(project, older, "Crashed", 2));

        var authed = _factory.CreateAuthorizedClient();
        var all = await authed.GetFromJsonAsync<PagedResult<SessionSummary>>($"/sessions?projectId={project}", Json);
        var withTimeline = await authed.GetFromJsonAsync<PagedResult<SessionSummary>>($"/sessions?projectId={project}&withTimeline=true", Json);
        var crashed = await authed.GetFromJsonAsync<PagedResult<SessionSummary>>($"/sessions?projectId={project}&status=Crashed", Json);

        Assert.Equal([newer, older], all!.Items.Select(s => s.Id));
        Assert.Equal([newer], withTimeline!.Items.Select(s => s.Id));
        Assert.Equal([older], crashed!.Items.Select(s => s.Id));
    }

    [Fact]
    public async Task ProjectReadKey_OnlySeesItsOwnSessionsTimelinesAndScreenshots()
    {
        using var factory = new ServerAppFactory();
        var admin = factory.CreateAuthorizedClient();
        var a = await ProjectApi.CreateAsync(admin, "tl-scope-a");
        var b = await ProjectApi.CreateAsync(admin, "tl-scope-b");

        var ingest = factory.CreateClient();
        ingest.DefaultRequestHeaders.Add("X-IntelligenceKit-Key", a.ProjectKey);
        var sessionId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        await ingest.PostAsJsonAsync("/events", SessionStart("tl-scope-a", sessionId));
        await ingest.PostAsJsonAsync("/events", TimelineBatch("tl-scope-a", sessionId, 0, [("screenshot", "Screenshot", DateTime.UtcNow)], batchId));
        var jpeg = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);
        jpeg.Headers.ContentType = new("image/jpeg");
        (await ingest.PostAsync($"/events/{batchId}/screenshot", jpeg)).EnsureSuccessStatusCode();

        var readerA = factory.CreateClientWithToken(a.ReadKey);
        var readerB = factory.CreateClientWithToken(b.ReadKey);

        var timeline = await TimelineAsync(readerA, sessionId);
        Assert.Equal(batchId, timeline.Items.Single().ScreenshotId);
        Assert.Equal(HttpStatusCode.OK, (await readerA.GetAsync($"/events/{batchId}/screenshot")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await readerB.GetAsync($"/sessions/{sessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await readerB.GetAsync($"/sessions/{sessionId}/timeline")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await readerB.GetAsync($"/events/{batchId}/screenshot")).StatusCode);
        var listB = await readerB.GetFromJsonAsync<PagedResult<SessionSummary>>("/sessions", Json);
        Assert.Empty(listB!.Items);
    }

    [Fact]
    public async Task TimelineEntries_ArePushedLive()
    {
        const string project = "tl-live";
        var sessionId = Guid.NewGuid();
        await _factory.CreateClient().PostAsJsonAsync("/events", SessionStart(project, sessionId));

        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/events"), o =>
            {
                o.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.AccessTokenProvider = () => Task.FromResult<string?>(ServerAppFactory.TestToken);
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()))
            .Build();

        var received = new TaskCompletionSource<TimelinePush>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<TimelinePush>("timelineReceived", push =>
        {
            if (push.SessionId == sessionId)
                received.TrySetResult(push);
        });
        await hub.StartAsync();

        await _factory.CreateClient().PostAsJsonAsync("/events",
            TimelineBatch(project, sessionId, 7, [("user", "pay_clicked", DateTime.UtcNow)]));

        var push = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var item = Assert.Single(push.Items);
        Assert.Equal("pay_clicked", item.Message);
        Assert.Equal(7, item.Sequence);
    }

    [Fact]
    public async Task Retention_DeletesOldTimelineEntries()
    {
        using var factory = ServerAppFactory.CreateWithRetention(days: 30);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IntelligenceDbContext>();

        TimelineEntry Entry(DateTime receivedAt) => new()
        {
            Id = Guid.NewGuid(), ProjectId = "p", SessionId = Guid.NewGuid(), BatchId = Guid.NewGuid(),
            Category = "navigation", Message = "Home", Timestamp = receivedAt, ReceivedAt = receivedAt,
        };
        db.TimelineEntries.AddRange(Entry(DateTime.UtcNow.AddDays(-60)), Entry(DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        await scope.ServiceProvider.GetRequiredService<RetentionSweeper>().SweepAsync();

        Assert.Equal(1, await db.TimelineEntries.CountAsync());
    }
}
