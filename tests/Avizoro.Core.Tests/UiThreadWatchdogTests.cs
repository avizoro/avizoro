using System.Collections.Concurrent;
using Avizoro.Core.Configuration;
using Avizoro.Core.Diagnostics;
using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;

namespace Avizoro.Core.Tests;

/// <summary>A UI thread that either answers immediately or is "frozen" until released.</summary>
internal sealed class FakeUiDispatcher : IUiThreadDispatcher
{
    private readonly ConcurrentQueue<Action> _held = new();

    public volatile bool Frozen;

    public void Post(Action action)
    {
        if (Frozen)
            _held.Enqueue(action);
        else
            ThreadPool.QueueUserWorkItem(_ => action());
    }

    public void Unfreeze()
    {
        Frozen = false;
        while (_held.TryDequeue(out var action))
            action();
    }

    public string? CaptureUiThreadStack() => "at App.MainPage.OnHeavyClick()";
}

internal sealed class RecordingKit : IAvizoro
{
    public ConcurrentQueue<AvizoroEvent> Tracked { get; } = new();

    public Task TrackAsync(AvizoroEvent avizoroEvent)
    {
        Tracked.Enqueue(avizoroEvent);
        return Task.CompletedTask;
    }

    public Task TrackExceptionAsync(Exception exception) => Task.CompletedTask;
    public Task TrackExceptionAsync(ExceptionInfo exception) => Task.CompletedTask;
    public Task TrackLogAsync(SeverityLevel level, string message, IDictionary<string, string>? data = null) => Task.CompletedTask;
    public void AddBreadcrumb(string message, string category = BreadcrumbCategories.Custom, SeverityLevel level = SeverityLevel.Information, IDictionary<string, string>? data = null) { }
    public void SetUser(string? userId) { }
    public void SetTag(string key, string? value) { }
    public Task CaptureCrashAsync(ExceptionInfo exception) => Task.CompletedTask;
}

public class UiThreadWatchdogTests
{
    private static readonly AvizoroOptions FastOptions = new() { AnrThreshold = TimeSpan.FromMilliseconds(150) };

    [Fact]
    public async Task ResponsiveUi_NeverReports()
    {
        var kit = new RecordingKit();
        using var watchdog = new UiThreadWatchdog(kit, new FakeUiDispatcher(), FastOptions);

        watchdog.Start();
        await Task.Delay(700);

        Assert.Empty(kit.Tracked);
    }

    [Fact]
    public async Task FrozenUi_ReportsOnce_WithStackAndMechanismTag()
    {
        var kit = new RecordingKit();
        var ui = new FakeUiDispatcher { Frozen = true };
        using var watchdog = new UiThreadWatchdog(kit, ui, FastOptions);

        watchdog.Start();
        await WaitUntil(() => !kit.Tracked.IsEmpty);
        await Task.Delay(500); // still frozen: must not report again

        var anr = Assert.Single(kit.Tracked);
        Assert.Equal(EventType.Exception, anr.EventType);
        Assert.Equal(UiThreadWatchdog.AnrExceptionType, anr.Exception!.Type);
        Assert.Contains("OnHeavyClick", anr.Exception.StackTrace);
        Assert.Equal("anr", anr.Tags["mechanism"]);
    }

    [Fact]
    public async Task SecondFreeze_AfterRecovery_IsReportedAgain()
    {
        var kit = new RecordingKit();
        var ui = new FakeUiDispatcher { Frozen = true };
        using var watchdog = new UiThreadWatchdog(kit, ui, FastOptions);

        watchdog.Start();
        await WaitUntil(() => kit.Tracked.Count == 1);
        ui.Unfreeze();
        await Task.Delay(300);

        ui.Frozen = true;
        await WaitUntil(() => kit.Tracked.Count == 2);
    }

    [Fact]
    public async Task Paused_DoesNotReport()
    {
        var kit = new RecordingKit();
        var ui = new FakeUiDispatcher();
        using var watchdog = new UiThreadWatchdog(kit, ui, FastOptions);

        watchdog.Start();
        watchdog.Pause();
        await Task.Delay(700); // let any in-flight round finish
        ui.Frozen = true;
        await Task.Delay(500);

        Assert.Empty(kit.Tracked);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (condition())
                return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Condition was never met.");
    }
}
