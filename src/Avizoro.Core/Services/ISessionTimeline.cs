using Avizoro.Core.Models;

namespace Avizoro.Core.Services;

/// <summary>
/// Records the activity of the current session (see <c>AvizoroOptions.EnableSessionTimeline</c>)
/// and ships it in batches through the store-and-forward queue, so the dashboard can show a session
/// as a timeline. Breadcrumbs added to the trail are recorded automatically; call it directly only
/// for things that aren't breadcrumbs, such as screenshots.
/// </summary>
public interface ISessionTimeline
{
    /// <summary>Adds an entry to the current session's timeline. No-op without a live, recorded session.</summary>
    void Record(Breadcrumb breadcrumb);

    /// <summary>
    /// Adds a <see cref="BreadcrumbCategories.Screenshot"/> entry with the given JPEG/PNG bytes.
    /// Returns false when nothing was recorded (no session, sampled out, or over the entry limit).
    /// </summary>
    Task<bool> RecordScreenshotAsync(byte[] image, string? screen = null);

    /// <summary>
    /// Writes pending entries to the local queue and, when <paramref name="upload"/> is true,
    /// drains the queue. Crash handlers pass false: the process is dying, the next launch uploads.
    /// </summary>
    Task FlushAsync(bool upload = true);
}
