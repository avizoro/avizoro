using Avizoro.Core.Providers;

namespace Avizoro.Maui.Diagnostics;

/// <summary>
/// Holds the most recently captured screen as a JPEG. Single-slot and
/// thread-safe: capture writes, crash/exception paths read.
/// </summary>
public sealed class LastScreenBuffer : ILastScreenProvider
{
    private readonly object _lock = new();
    private byte[]? _jpeg;

    public void Update(byte[] jpeg)
    {
        lock (_lock)
        {
            _jpeg = jpeg;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _jpeg = null;
        }
    }

    public byte[]? GetLastScreenshot()
    {
        lock (_lock)
        {
            return _jpeg;
        }
    }
}
