using Avizoro.Core.Diagnostics;
using Avizoro.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Avizoro.Maui.Extensions;

public static class HttpClientBuilderExtensions
{
    /// <summary>
    /// Records every request made through this client as an <c>http</c> breadcrumb
    /// and a performance span:
    /// <c>builder.Services.AddHttpClient&lt;MyApi&gt;().AddAvizoroHandler();</c>
    /// </summary>
    public static IHttpClientBuilder AddAvizoroHandler(this IHttpClientBuilder builder)
        => builder.AddHttpMessageHandler(sp => new AvizoroHttpHandler(
            sp.GetRequiredService<IAvizoro>(),
            sp.GetService<IPerformanceMonitor>()));
}
