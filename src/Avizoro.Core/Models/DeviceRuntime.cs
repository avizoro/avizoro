namespace Avizoro.Core.Models;

/// <summary>
/// A point-in-time snapshot of the device's runtime state when an event was
/// captured. This is the context that turns a bare stack trace into a
/// reproducible situation ("crashed at 18% battery on 4G, 512 MB used").
/// Every field is nullable: not all platforms expose every metric.
/// </summary>
public class DeviceRuntime
{
    /// <summary>Managed memory currently in use, in bytes.</summary>
    public long? MemoryUsedBytes { get; set; }

    /// <summary>Battery charge, 0.0–1.0.</summary>
    public double? BatteryLevel { get; set; }

    /// <summary>e.g. "Charging", "Discharging", "Full".</summary>
    public string? BatteryState { get; set; }

    /// <summary>e.g. "Internet", "None", "Local".</summary>
    public string? NetworkAccess { get; set; }

    /// <summary>Active connection profiles, e.g. "WiFi", "Cellular".</summary>
    public string? ConnectionProfiles { get; set; }

    /// <summary>The screen/page the user was on, if known.</summary>
    public string? Screen { get; set; }
}
