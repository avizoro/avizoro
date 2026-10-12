using Avizoro.Core.Enums;
using Avizoro.Core.Models;
using Avizoro.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Avizoro.Extensions.Logging;

/// <summary>Controls what the Avizoro logger records.</summary>
public sealed class AvizoroLoggerOptions
{
    /// <summary>Entries at or above this level become breadcrumbs (default Information).</summary>
    public LogLevel MinimumBreadcrumbLevel { get; set; } = LogLevel.Information;

    /// <summary>Entries at or above this level are sent as events (default Error).</summary>
    public LogLevel MinimumEventLevel { get; set; } = LogLevel.Error;

    /// <summary>
    /// Category prefixes that are never recorded. Defaults exclude Avizoro
    /// itself and the SDK's own HTTP client, so reporting can't feed back into itself.
    /// </summary>
    public List<string> ExcludedCategoryPrefixes { get; set; } =
    [
        "Avizoro.Core.",
        "Avizoro.Extensions.",
        "Avizoro.Hosting.",
        "Avizoro.Maui.",
        "System.Net.Http.HttpClient.IAvizoroClient",
    ];
}

/// <summary>
/// <see cref="ILoggerProvider"/> that feeds <c>Microsoft.Extensions.Logging</c> into
/// Avizoro: entries become breadcrumbs, and errors become events (as an
/// exception event when one is attached, otherwise a log event grouped by its
/// message template — so "Order {Id} failed" is one issue, not one per id).
/// <see cref="IAvizoro"/> is resolved lazily from the container, because
/// loggers are created before most services exist.
/// </summary>
[ProviderAlias("Avizoro")]
public sealed class AvizoroLoggerProvider : ILoggerProvider
{
    private readonly IServiceProvider? _services;
    private IAvizoro? _kit;

    public AvizoroLoggerProvider(IServiceProvider services, AvizoroLoggerOptions options)
    {
        _services = services;
        Options = options;
    }

    /// <summary>For hosts without DI: wire a kit directly.</summary>
    public AvizoroLoggerProvider(IAvizoro kit, AvizoroLoggerOptions? options = null)
    {
        _kit = kit;
        Options = options ?? new AvizoroLoggerOptions();
    }

    public AvizoroLoggerOptions Options { get; }

    internal IAvizoro? Kit => _kit ??= _services?.GetService<IAvizoro>();

    public ILogger CreateLogger(string categoryName) => new AvizoroLogger(this, categoryName);

    public void Dispose()
    {
    }
}

internal sealed class AvizoroLogger : ILogger
{
    private const string OriginalFormatKey = "{OriginalFormat}";

    // Guards against feedback loops: anything logged while we are recording a log
    // entry (e.g. by the HTTP stack sending it) is ignored on this thread.
    [ThreadStatic]
    private static bool _recording;

    private readonly AvizoroLoggerProvider _provider;
    private readonly string _category;
    private readonly bool _excluded;

    public AvizoroLogger(AvizoroLoggerProvider provider, string category)
    {
        _provider = provider;
        _category = category;
        _excluded = provider.Options.ExcludedCategoryPrefixes.Any(p => category.StartsWith(p, StringComparison.Ordinal));
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
        => !_excluded && logLevel != LogLevel.None &&
           logLevel >= (LogLevel)Math.Min((int)_provider.Options.MinimumBreadcrumbLevel, (int)_provider.Options.MinimumEventLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (_recording || !IsEnabled(logLevel) || _provider.Kit is not { } kit)
            return;

        _recording = true;
        try
        {
            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is not null)
                message = exception.Message;

            var level = logLevel.ToSeverity();
            var (template, properties) = Structure(state);

            if (logLevel >= _provider.Options.MinimumBreadcrumbLevel)
            {
                var data = new Dictionary<string, string> { ["category"] = _category };
                if (eventId.Id != 0)
                    data["event_id"] = eventId.Id.ToString();
                kit.AddBreadcrumb(message, BreadcrumbCategories.Log, level, data);
            }

            // An exception another integration already reported (e.g. request
            // middleware) is not reported again when it gets logged.
            if (logLevel >= _provider.Options.MinimumEventLevel &&
                (exception is null || !ExceptionCapture.IsCaptured(exception)))
            {
                if (exception is not null)
                    ExceptionCapture.MarkCaptured(exception);
                _ = SafeTrackAsync(kit, BuildEvent(level, message, template, properties, exception, eventId));
            }
        }
        catch
        {
            // A logger must never throw into the caller.
        }
        finally
        {
            _recording = false;
        }
    }

    private AvizoroEvent BuildEvent(SeverityLevel level, string message, string? template,
        IReadOnlyList<KeyValuePair<string, object?>> properties, Exception? exception, EventId eventId)
    {
        var e = new AvizoroEvent
        {
            EventType = exception is null ? EventType.Log : EventType.Exception,
            Level = level,
            Message = message,
            Exception = exception is null ? null : ExceptionInfo.FromException(exception),
            Tags = { ["logger"] = _category }
        };

        foreach (var (key, value) in properties)
            e.Data[key] = value?.ToString();
        if (template is not null)
            e.Data["message_template"] = template;
        if (eventId.Id != 0)
            e.Data["event_id"] = eventId.Id;

        // Group plain log events by (category, template) rather than the formatted text.
        if (exception is null && template is not null)
            e.Fingerprint = ["log", _category, template];

        return e;
    }

    private static (string? Template, IReadOnlyList<KeyValuePair<string, object?>> Properties) Structure<TState>(TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> pairs)
            return (null, []);

        string? template = null;
        var properties = new List<KeyValuePair<string, object?>>(pairs.Count);
        foreach (var pair in pairs)
        {
            if (pair.Key == OriginalFormatKey)
                template = pair.Value as string;
            else
                properties.Add(pair);
        }
        return (template, properties);
    }

    private static async Task SafeTrackAsync(IAvizoro kit, AvizoroEvent e)
    {
        try
        {
            await kit.TrackAsync(e).ConfigureAwait(false);
        }
        catch
        {
        }
    }
}

internal static class LogLevelMapping
{
    /// <summary>SeverityLevel mirrors LogLevel's names and order.</summary>
    public static SeverityLevel ToSeverity(this LogLevel level) => level switch
    {
        LogLevel.Trace => SeverityLevel.Trace,
        LogLevel.Debug => SeverityLevel.Debug,
        LogLevel.Information => SeverityLevel.Information,
        LogLevel.Warning => SeverityLevel.Warning,
        LogLevel.Error => SeverityLevel.Error,
        _ => SeverityLevel.Critical
    };
}
