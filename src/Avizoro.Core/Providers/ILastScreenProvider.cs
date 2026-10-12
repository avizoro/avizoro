namespace Avizoro.Core.Providers;

/// <summary>
/// Supplies the most recent captured screen as a JPEG. The capture happens
/// proactively (e.g. on navigation) and is kept in memory, so attaching it to a
/// crash never touches the UI thread while the app is dying. Returns null when
/// screen capture is disabled or nothing has been captured yet.
/// </summary>
public interface ILastScreenProvider
{
    byte[]? GetLastScreenshot();
}
