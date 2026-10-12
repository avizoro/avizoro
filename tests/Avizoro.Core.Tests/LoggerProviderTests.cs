using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;
using Avizoro.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avizoro.Core.Tests;

public class LoggerProviderTests
{
    private sealed class CapturingKit : IAvizoro
    {
        public List<AvizoroEvent> Events { get; } = new();
        public List<(string Message, SeverityLevel Level, IDictionary<string, string>? Data)> Crumbs { get; } = new();

        public Task TrackAsync(AvizoroEvent avizoroEvent)
        {
            lock (Events) Events.Add(avizoroEvent);
            return Task.CompletedTask;
        }

        public void AddBreadcrumb(string message, string category = BreadcrumbCategories.Custom,
            SeverityLevel level = SeverityLevel.Information, IDictionary<string, string>? data = null)
            => Crumbs.Add((message, level, data));

        public Task TrackExceptionAsync(Exception exception) => Task.CompletedTask;
        public Task TrackExceptionAsync(ExceptionInfo exception) => Task.CompletedTask;
        public Task TrackLogAsync(SeverityLevel level, string message, IDictionary<string, string>? data = null) => Task.CompletedTask;
        public void SetUser(string? userId) { }
        public void SetTag(string key, string? value) { }
        public Task CaptureCrashAsync(ExceptionInfo exception) => Task.CompletedTask;
    }

    private static (ILogger Logger, CapturingKit Kit) Create(Action<AvizoroLoggerOptions>? configure = null, string category = "MyApp.Checkout")
    {
        var kit = new CapturingKit();
        var services = new ServiceCollection()
            .AddSingleton<IAvizoro>(kit)
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddAvizoro(configure))
            .BuildServiceProvider();
        return (services.GetRequiredService<ILoggerFactory>().CreateLogger(category), kit);
    }

    [Fact]
    public void Information_BecomesBreadcrumb_NotEvent()
    {
        var (logger, kit) = Create();

        logger.LogInformation("Cart has {Count} items", 3);
        logger.LogDebug("too chatty");

        var crumb = Assert.Single(kit.Crumbs);
        Assert.Equal("Cart has 3 items", crumb.Message);
        Assert.Equal("MyApp.Checkout", crumb.Data!["category"]);
        Assert.Empty(kit.Events);
    }

    [Fact]
    public void Error_BecomesLogEvent_GroupedByTemplate_WithProperties()
    {
        var (logger, kit) = Create();

        logger.LogError("Order {OrderId} failed", 42);
        logger.LogError("Order {OrderId} failed", 43);

        Assert.Equal(2, kit.Events.Count);
        var e = kit.Events[0];
        Assert.Equal(EventType.Log, e.EventType);
        Assert.Equal(SeverityLevel.Error, e.Level);
        Assert.Equal("Order 42 failed", e.Message);
        Assert.Equal("42", e.Data["OrderId"]);
        Assert.Equal("Order {OrderId} failed", e.Data["message_template"]);
        Assert.Equal("MyApp.Checkout", e.Tags["logger"]);
        Assert.Equal(kit.Events[0].Fingerprint, kit.Events[1].Fingerprint);
    }

    [Fact]
    public void ErrorWithException_BecomesExceptionEvent()
    {
        var (logger, kit) = Create();

        logger.LogCritical(new TimeoutException("gateway"), "Payment failed");

        var e = Assert.Single(kit.Events);
        Assert.Equal(EventType.Exception, e.EventType);
        Assert.Equal(SeverityLevel.Critical, e.Level);
        Assert.Equal("System.TimeoutException", e.Exception!.Type);
        Assert.Null(e.Fingerprint); // exceptions keep stack-based grouping
    }

    [Fact]
    public void Thresholds_AreConfigurable()
    {
        var (logger, kit) = Create(o =>
        {
            o.MinimumBreadcrumbLevel = LogLevel.Warning;
            o.MinimumEventLevel = LogLevel.Warning;
        });

        logger.LogInformation("ignored");
        logger.LogWarning("low disk");

        Assert.Single(kit.Crumbs);
        Assert.Single(kit.Events);
    }

    [Fact]
    public void OwnCategories_AreExcluded_ToAvoidFeedbackLoops()
    {
        var (logger, kit) = Create(category: "System.Net.Http.HttpClient.IAvizoroClient.LogicalHandler");

        logger.LogError("send failed");

        Assert.Empty(kit.Crumbs);
        Assert.Empty(kit.Events);
    }
}
