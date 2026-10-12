using System.Text;
using Avizoro.Core.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace Avizoro.Maui.Diagnostics;

/// <summary>
/// MAUI bridge for the ANR watchdog. Posts pings through the MAUI main-thread
/// dispatcher; on Android it also snapshots the main (Looper) thread's stack
/// while it's blocked — Java/native frames, which show what the UI thread was
/// stuck in. iOS offers no API to read another thread's stack, so it's omitted.
/// </summary>
public sealed class MauiUiThreadDispatcher : IUiThreadDispatcher
{
    public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);

    public string? CaptureUiThreadStack()
    {
#if ANDROID
        var frames = Android.OS.Looper.MainLooper?.Thread?.GetStackTrace();
        if (frames is null || frames.Length == 0)
            return null;

        var sb = new StringBuilder();
        foreach (var frame in frames)
            sb.Append("   at ").AppendLine(frame.ToString());
        return sb.ToString();
#else
        return null;
#endif
    }
}
