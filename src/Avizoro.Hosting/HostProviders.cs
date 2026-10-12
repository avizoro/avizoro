using System.Diagnostics;
using System.Runtime.InteropServices;
using Avizoro.Core.Models;
using Avizoro.Core.Providers;

namespace Avizoro.Hosting;

/// <summary>Describes the machine from <see cref="RuntimeInformation"/> / <see cref="Environment"/>.</summary>
public sealed class EnvironmentDeviceContextProvider : IDeviceContextProvider
{
    public string Platform { get; } =
        System.OperatingSystem.IsBrowser() ? "Browser"
        : System.OperatingSystem.IsWindows() ? "Windows"
        : System.OperatingSystem.IsMacOS() ? "macOS"
        : System.OperatingSystem.IsLinux() ? "Linux"
        : RuntimeInformation.OSDescription;

    public string DeviceName { get; } = SafeMachineName();

    public string Model { get; } = $"{RuntimeInformation.OSArchitecture} · {RuntimeInformation.FrameworkDescription}";

    public string Manufacturer => string.Empty;

    public string OperatingSystem { get; } = RuntimeInformation.OSDescription;

    private static string SafeMachineName()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return string.Empty;
        }
    }
}

/// <summary>Runtime snapshot from the process: memory in use (no battery/network on generic hosts).</summary>
public sealed class ProcessRuntimeContextProvider : IRuntimeContextProvider
{
    public DeviceRuntime Capture() => new()
    {
        MemoryUsedBytes = OperatingSystem.IsBrowser() ? GC.GetTotalMemory(false) : Environment.WorkingSet
    };
}

/// <summary>For hosts without screens to capture.</summary>
public sealed class NullLastScreenProvider : ILastScreenProvider
{
    public byte[]? GetLastScreenshot() => null;
}

/// <summary>Anonymous installation id persisted as a small file next to the offline queue.</summary>
public sealed class FileInstallationIdProvider : IInstallationIdProvider
{
    private readonly string _path;
    private string? _cached;

    public FileInstallationIdProvider(string directory) => _path = Path.Combine(directory, "installation-id");

    public string GetInstallationId()
    {
        if (_cached is not null)
            return _cached;

        try
        {
            if (File.Exists(_path))
            {
                var existing = File.ReadAllText(_path).Trim();
                if (existing.Length > 0)
                    return _cached = existing;
            }

            var id = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, id);
            return _cached = id;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return _cached = Guid.NewGuid().ToString("N");
        }
    }
}

internal static class HostDefaults
{
    public static string StorageDirectory(string projectId)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
            root = Path.GetTempPath();
        var directory = Path.Combine(root, "Avizoro", Sanitize(projectId));
        MoveLegacyDirectory(Path.Combine(root, "IntelligenceKit", Sanitize(projectId)), directory);
        return directory;
    }

    /// <summary>Carries the queue and installation id over from a 1.x (IntelligenceKit) install.</summary>
    private static void MoveLegacyDirectory(string legacy, string current)
    {
        try
        {
            if (!Directory.Exists(legacy) || Directory.Exists(current))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(current)!);
            Directory.Move(legacy, current);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a fresh directory only loses unsent events.
        }
    }

    public static (string Name, string Version) EntryApplication()
    {
        var entry = System.Reflection.Assembly.GetEntryAssembly();
        var name = entry?.GetName().Name ?? Process.GetCurrentProcess().ProcessName;
        var informational = entry?
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;

        // Drop the "+<commit sha>" suffix SourceLink appends.
        var version = informational?.Split('+')[0] ?? entry?.GetName().Version?.ToString() ?? "0.0.0";
        return (name, version);
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
