namespace Avizoro.Core.Providers;

public interface IDeviceContextProvider
{
    string Platform { get; }

    string DeviceName { get; }

    string Model { get; }

    string Manufacturer { get; }

    string OperatingSystem { get; }
}
