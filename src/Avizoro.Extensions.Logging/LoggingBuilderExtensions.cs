using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Avizoro.Extensions.Logging;

public static class LoggingBuilderExtensions
{
    /// <summary>
    /// Sends log entries to Avizoro: breadcrumbs from Information, events
    /// from Error (tunable via <paramref name="configure"/>). Requires an
    /// <c>IAvizoro</c> registered by a platform SDK (e.g. <c>UseAvizoro</c>).
    /// Safe to call more than once.
    /// </summary>
    public static ILoggingBuilder AddAvizoro(this ILoggingBuilder builder, Action<AvizoroLoggerOptions>? configure = null)
    {
        var options = new AvizoroLoggerOptions();
        configure?.Invoke(options);

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, AvizoroLoggerProvider>(
            sp => new AvizoroLoggerProvider(sp, options)));
        return builder;
    }
}
