using Avizoro.Core.Models;
using Avizoro.Core.Providers;
using Avizoro.Maui.Diagnostics;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;

namespace Avizoro.Maui.Providers;

/// <summary>
/// Captures live device state via MAUI Essentials. Each metric is guarded on
/// its own so a single unsupported/permission-gated reading (e.g. battery on an
/// emulator) never blanks out the rest.
/// </summary>
public class MauiRuntimeContextProvider : IRuntimeContextProvider
{
    private readonly NavigationTracker _navigation;

    public MauiRuntimeContextProvider(NavigationTracker navigation)
    {
        _navigation = navigation;
    }

    public DeviceRuntime Capture()
    {
        var runtime = new DeviceRuntime
        {
            MemoryUsedBytes = GC.GetTotalMemory(forceFullCollection: false),
            Screen = _navigation.CurrentScreen
        };

        try
        {
            runtime.BatteryLevel = Battery.Default.ChargeLevel;
            runtime.BatteryState = Battery.Default.State.ToString();
        }
        catch
        {
            // Battery not available on this platform/emulator.
        }

        try
        {
            var connectivity = Connectivity.Current;
            runtime.NetworkAccess = connectivity.NetworkAccess.ToString();
            runtime.ConnectionProfiles = string.Join(", ", connectivity.ConnectionProfiles);
        }
        catch
        {
            // Connectivity not available.
        }

        return runtime;
    }
}
