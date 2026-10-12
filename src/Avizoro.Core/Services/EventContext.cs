using Avizoro.Core.Configuration;
using Avizoro.Core.Models;
using Avizoro.Core.Providers;

namespace Avizoro.Core.Services;

/// <summary>
/// Stamps the lightweight app/device identity onto SDK-generated telemetry
/// (session updates, performance batches) that bypasses the full
/// <see cref="AvizoroService"/> enrichment — no breadcrumbs, runtime
/// snapshot, hooks or sampling, which only make sense for error events.
/// </summary>
internal static class EventContext
{
    public static void Stamp(AvizoroEvent e, AvizoroOptions options, IDeviceContextProvider device)
    {
        e.ProjectId = options.ProjectId;
        e.ApplicationName = options.ApplicationName;
        e.ApplicationVersion = options.ApplicationVersion;
        e.Environment = options.Environment;
        e.Release = string.IsNullOrWhiteSpace(options.Release) ? options.ApplicationVersion : options.Release;
        e.Platform = Safe(() => device.Platform);
        e.DeviceName = Safe(() => device.DeviceName);
        e.DeviceModel = Safe(() => device.Model);
        e.Manufacturer = Safe(() => device.Manufacturer);
        e.OperatingSystem = Safe(() => device.OperatingSystem);
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return string.Empty;
        }
    }
}
