using System.Net;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;
using Avizoro.Core.Storage;
using Avizoro.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Avizoro.Sdk.Tests;

// AvizoroSdk is process-wide static state: keep these tests sequential.
[Collection("sdk-static")]
public class HostingTests
{
    private const string Dsn = "http://key@ik.test:7099/desktop-app";

    private static AvizoroInstance Init(FakeIngest ingest, string storage, Action<AvizoroHostOptions>? configure = null)
        => AvizoroSdk.Init(Dsn,
            o =>
            {
                o.OfflineStorePath = storage;
                o.EnableCrashHandlers = false; // don't hook the test runner's AppDomain
                configure?.Invoke(o);
            },
            (services, _) => services.AddHttpClient<IAvizoroClient, HttpAvizoroClient>()
                .ConfigurePrimaryHttpMessageHandler(() => ingest));

    [Fact]
    public async Task Init_TracksEvents_AndRunsASessionUntilDisposed()
    {
        var ingest = new FakeIngest();
        var instance = Init(ingest, TempDir.Create());

        Assert.Same(instance.Kit, AvizoroSdk.Current);
        await instance.Kit.TrackLogAsync(SeverityLevel.Warning, "disk almost full");

        var log = await ingest.WaitForAsync(e => e.EventType == EventType.Log, "log event");
        Assert.Equal("desktop-app", log.ProjectId);
        Assert.False(string.IsNullOrEmpty(log.Platform));
        await ingest.WaitForAsync(e => e.Session is { Init: true }, "session start");

        await instance.DisposeAsync();

        await ingest.WaitForAsync(e => e.Session is { Status: SessionStatus.Exited }, "session end");
        Assert.Null(AvizoroSdk.Current);
    }

    [Fact]
    public async Task OfflineEvents_ArePersisted_AndDeliveredOnNextStart()
    {
        var storage = TempDir.Create();

        var offline = new FakeIngest { Status = HttpStatusCode.ServiceUnavailable };
        var first = Init(offline, storage, o => o.EnableAutoSessionTracking = false);
        await first.Kit.TrackExceptionAsync(new InvalidOperationException("while offline"));
        await first.DisposeAsync();
        Assert.True(Directory.EnumerateFiles(Path.Combine(storage, "queue"), "*.json").Any());

        var online = new FakeIngest();
        var second = Init(online, storage, o => o.EnableAutoSessionTracking = false);
        var delivered = await online.WaitForAsync(e => e.Exception?.Message == "while offline", "queued exception");
        Assert.Equal("System.InvalidOperationException", delivered.Exception!.Type);
        await second.DisposeAsync();
    }

    [Fact]
    public async Task InstallationId_IsStableAcrossRuns()
    {
        var storage = TempDir.Create();
        var ingest = new FakeIngest();

        var first = Init(ingest, storage);
        var id1 = (await ingest.WaitForAsync(e => e.Session is not null, "session")).Session!.DistinctId;
        await first.DisposeAsync();

        var ingest2 = new FakeIngest();
        var second = Init(ingest2, storage);
        var id2 = (await ingest2.WaitForAsync(e => e.Session is not null, "session")).Session!.DistinctId;
        await second.DisposeAsync();

        Assert.False(string.IsNullOrEmpty(id1));
        Assert.Equal(id1, id2);
    }
}

public class FileEventStoreTests
{
    [Fact]
    public async Task ReturnsOldestFirst_ReplacesById_AndDeletes()
    {
        var store = new FileEventStore(TempDir.Create());
        var older = new AvizoroEvent { Message = "older", Timestamp = DateTime.UtcNow.AddMinutes(-5) };
        var newer = new AvizoroEvent { Message = "newer", Timestamp = DateTime.UtcNow };

        await store.SaveAsync(newer);
        await store.SaveAsync(older);
        older.Message = "older v2";
        await store.SaveAsync(older);

        var pending = await store.GetPendingAsync();
        Assert.Equal(["older v2", "newer"], pending.Select(e => e.Message));

        await store.DeleteAsync(older.Id);
        Assert.Equal(1, await store.CountAsync());
    }

    [Fact]
    public async Task CorruptEntry_IsDropped_NotBlocking()
    {
        var dir = TempDir.Create();
        var store = new FileEventStore(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "00000000000000000001_bad.json"), "{ not json");
        await store.SaveAsync(new AvizoroEvent { Message = "good" });

        var pending = await store.GetPendingAsync();

        Assert.Equal("good", Assert.Single(pending).Message);
        Assert.Equal(1, await store.CountAsync());
    }
}

public class ScopeTests
{
    [Fact]
    public async Task ConcurrentScopes_KeepTheirOwnBreadcrumbsAndTags()
    {
        var services = new ServiceCollection();
        services.AddAvizoroCore("http://k@h/p", o =>
        {
            o.UseInMemoryStore = true;
            o.EnableAutoSessionTracking = false;
            o.EnablePerformanceMonitoring = false;
        });
        var captured = new List<AvizoroEvent>();
        services.AddSingleton<IEventStore>(new CapturingStore(captured));
        using var provider = services.BuildServiceProvider();
        var kit = provider.GetRequiredService<IAvizoro>();

        async Task Request(string name)
        {
            using var scope = AvizoroScope.Push();
            scope.SetTag("request", name);
            kit.AddBreadcrumb($"step in {name}");
            await Task.Yield();
            await kit.TrackLogAsync(SeverityLevel.Error, $"failed {name}");
        }

        await Task.WhenAll(Request("a"), Request("b"));

        var a = captured.Single(e => e.Message == "failed a");
        Assert.Equal("a", a.Tags["request"]);
        Assert.All(a.Breadcrumbs, b => Assert.DoesNotContain("in b", b.Message));
        Assert.Contains(a.Breadcrumbs, b => b.Message == "step in a");
        Assert.Null(AvizoroScope.Current);
    }

    private sealed class CapturingStore(List<AvizoroEvent> captured) : IEventStore
    {
        public Task SaveAsync(AvizoroEvent avizoroEvent)
        {
            lock (captured) captured.Add(avizoroEvent);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AvizoroEvent>> GetPendingAsync(int max = 50) => Task.FromResult<IReadOnlyList<AvizoroEvent>>([]);
        public Task DeleteAsync(Guid id) => Task.CompletedTask;
        public Task<int> CountAsync() => Task.FromResult(0);
    }
}
