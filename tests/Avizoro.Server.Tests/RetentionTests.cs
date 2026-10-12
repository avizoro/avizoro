using Avizoro.Server.Data;
using Avizoro.Server.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Avizoro.Server.Tests;

public class RetentionTests
{
    private static StoredEvent Event(string project, DateTime receivedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = project,
            Fingerprint = "fp",
            EventType = "Log",
            Timestamp = receivedAt,
            ReceivedAt = receivedAt,
        };

    private static StoredScreenshot Shot(DateTime receivedAt)
        => new()
        {
            EventId = Guid.NewGuid(),
            Jpeg = new byte[] { 1, 2, 3 },
            ReceivedAt = receivedAt,
        };

    private static Issue Issue(string fingerprint, DateTime lastSeen)
        => new()
        {
            Id = Guid.NewGuid(),
            ProjectId = "p",
            Fingerprint = fingerprint,
            EventType = "Log",
            EventCount = 1,
            FirstSeen = lastSeen,
            LastSeen = lastSeen,
        };

    [Fact]
    public async Task Sweep_DeletesDataOlderThanWindow_KeepsRecent()
    {
        using var factory = ServerAppFactory.CreateWithRetention(days: 30);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AvizoroDbContext>();

        var now = DateTime.UtcNow;
        db.Events.AddRange(Event("p", now.AddDays(-60)), Event("p", now.AddDays(-5)));
        db.Screenshots.AddRange(Shot(now.AddDays(-90)), Shot(now.AddDays(-1)));
        db.Issues.AddRange(Issue("stale", now.AddDays(-45)), Issue("fresh", now.AddDays(-2)));
        await db.SaveChangesAsync();

        var result = await scope.ServiceProvider.GetRequiredService<RetentionSweeper>().SweepAsync();

        Assert.True(result.Enabled);
        Assert.Equal(1, result.Events);
        Assert.Equal(1, result.Screenshots);
        Assert.Equal(1, result.Issues);

        // Exactly the recent rows survive.
        Assert.Equal(1, await db.Events.CountAsync());
        Assert.Equal(1, await db.Screenshots.CountAsync());
        Assert.Equal(1, await db.Issues.CountAsync());
    }

    [Fact]
    public async Task Sweep_WhenDisabled_DeletesNothing()
    {
        using var factory = ServerAppFactory.CreateWithRetentionDisabled();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AvizoroDbContext>();

        db.Events.Add(Event("p", DateTime.UtcNow.AddDays(-500)));
        await db.SaveChangesAsync();

        var result = await scope.ServiceProvider.GetRequiredService<RetentionSweeper>().SweepAsync();

        Assert.False(result.Enabled);
        Assert.Equal(0, result.Total);
        Assert.Equal(1, await db.Events.CountAsync()); // ancient row kept
    }

    [Fact]
    public async Task Sweep_WithNonPositiveDays_IsANoOp_NotAPurgeEverything()
    {
        // Enabled but Days=0 must never wipe the table — a retention foot-gun guard.
        using var factory = ServerAppFactory.CreateWithRetention(days: 0);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AvizoroDbContext>();

        db.Events.Add(Event("p", DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();

        var result = await scope.ServiceProvider.GetRequiredService<RetentionSweeper>().SweepAsync();

        Assert.False(result.Enabled);
        Assert.Equal(1, await db.Events.CountAsync());
    }
}
