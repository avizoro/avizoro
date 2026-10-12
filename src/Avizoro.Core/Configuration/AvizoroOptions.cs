using System.Text.RegularExpressions;
using Avizoro.Core.Models;

namespace Avizoro.Core.Configuration;

public class AvizoroOptions
{
    public string ApplicationName { get; set; } = string.Empty;

    public string ApplicationVersion { get; set; } = string.Empty;

    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Public project identifier used to route events to the right project on
    /// the server. Not a secret.
    /// </summary>
    public string ProjectKey { get; set; } = string.Empty;

    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Deployment environment, e.g. "production", "staging". Attached to every event.</summary>
    public string Environment { get; set; } = "production";

    /// <summary>
    /// Release/build identifier. When left empty, the application version is used.
    /// </summary>
    public string Release { get; set; } = string.Empty;

    /// <summary>Maximum breadcrumbs retained in the local ring buffer.</summary>
    public int BreadcrumbCapacity { get; set; } = 50;

    /// <summary>Automatically record a breadcrumb whenever a page appears.</summary>
    public bool EnableNavigationBreadcrumbs { get; set; } = true;

    /// <summary>
    /// Capture a downscaled screenshot of the current screen (proactively, on
    /// navigation) and attach the last one to crashes/exceptions. OFF by default:
    /// screenshots can contain personal data, so it's an explicit opt-in.
    /// </summary>
    public bool EnableScreenCapture { get; set; } = false;

    /// <summary>Longest edge of the stored screenshot, in pixels.</summary>
    public int ScreenCaptureMaxDimension { get; set; } = 640;

    /// <summary>JPEG quality for the stored screenshot, 0.0–1.0.</summary>
    public float ScreenCaptureJpegQuality { get; set; } = 0.6f;

    /// <summary>
    /// Page type names to never capture (e.g. "LoginPage", "PaymentPage"). When
    /// the user is on one of these, no screenshot is kept.
    /// </summary>
    public HashSet<string> ScreenCaptureExcludedPages { get; set; } = new();

    public bool EnableCrashReporting { get; set; } = true;

    public bool EnableDeviceInfo { get; set; } = true;

    /// <summary>
    /// Track app sessions automatically (start at launch, pause/resume with the app
    /// lifecycle) so the dashboard can show crash-free sessions/users per release.
    /// </summary>
    public bool EnableAutoSessionTracking { get; set; } = true;

    /// <summary>
    /// How long the app may stay in the background before returning to it starts a
    /// new session instead of continuing the previous one.
    /// </summary>
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Record the full activity of each session (navigation, user actions, HTTP calls,
    /// logs, screenshots) and send it to the server, not only the breadcrumbs attached to
    /// errors. The dashboard then shows every session as a chronological timeline, live
    /// while the app runs. Needs <see cref="EnableAutoSessionTracking"/>. OFF by default:
    /// it sends more data, and the trail can describe what the user did.
    /// </summary>
    public bool EnableSessionTimeline { get; set; } = false;

    /// <summary>How often recorded timeline entries are written to the queue and sent (also on background and every 50 entries).</summary>
    public TimeSpan SessionTimelineFlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Most entries recorded per session; later activity is dropped (a marker entry says so).</summary>
    public int SessionTimelineMaxEntries { get; set; } = 1000;

    /// <summary>Fraction (0.0–1.0) of sessions whose timeline is recorded. Decided once per session.</summary>
    public double SessionTimelineSampleRate { get; set; } = 1.0;

    /// <summary>
    /// Report "Application Not Responding" when the UI thread is blocked for longer
    /// than <see cref="AnrThreshold"/>.
    /// </summary>
    public bool EnableAnrDetection { get; set; } = true;

    /// <summary>How long the UI thread may be unresponsive before it counts as an ANR (default 5 s, like Android).</summary>
    public TimeSpan AnrThreshold { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Platform SDKs that host Microsoft.Extensions.Logging (e.g. MAUI) register the
    /// Avizoro logger provider automatically: Information+ logs become
    /// breadcrumbs and Error+ logs become events.
    /// </summary>
    public bool EnableLoggingIntegration { get; set; } = true;

    /// <summary>
    /// Measure app start, page load and (with the HTTP handler) request durations,
    /// and ship them as batched performance spans.
    /// </summary>
    public bool EnablePerformanceMonitoring { get; set; } = true;

    /// <summary>Fraction (0.0–1.0) of performance spans to keep.</summary>
    public double PerformanceSampleRate { get; set; } = 1.0;

    /// <summary>How often buffered spans are shipped (also on background / every 50 spans).</summary>
    public TimeSpan PerformanceFlushInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// On the next launch after a crash, ask the user what they were doing and send
    /// the answer linked to the crash (platform SDKs with UI). Off by default.
    /// </summary>
    public bool EnableCrashFeedbackPrompt { get; set; }

    public string CrashFeedbackTitle { get; set; } = "The app closed unexpectedly";

    public string CrashFeedbackMessage { get; set; } = "Tell us what you were doing so we can fix it (optional).";

    public string CrashFeedbackAccept { get; set; } = "Send";

    public string CrashFeedbackCancel { get; set; } = "Skip";

    /// <summary>
    /// Last chance to modify or drop an event before it is stored and sent. Runs
    /// after enrichment and before PII scrubbing. Return null to drop the event. If
    /// the callback throws, the event is sent unmodified. Also runs for crashes
    /// (keep it fast and side-effect free).
    /// </summary>
    public Func<AvizoroEvent, AvizoroEvent?>? BeforeSend { get; set; }

    /// <summary>Modify or drop (return null) a breadcrumb before it enters the trail.</summary>
    public Func<Breadcrumb, Breadcrumb?>? BeforeBreadcrumb { get; set; }

    /// <summary>
    /// Fraction (0.0–1.0) of non-fatal events (handled exceptions, logs, ANRs) to
    /// keep. Fatal crashes and session updates are never sampled out.
    /// </summary>
    public double SampleRate { get; set; } = 1.0;

    /// <summary>
    /// Mask personal data and secrets (emails, tokens, card numbers, sensitive keys)
    /// before events leave the device. On by default.
    /// </summary>
    public bool EnablePiiScrubbing { get; set; } = true;

    /// <summary>Extra key fragments whose values are always replaced by "[Filtered]".</summary>
    public List<string> ScrubbingSensitiveKeys { get; set; } = new();

    /// <summary>Extra patterns masked in free text (messages, breadcrumbs, string values).</summary>
    public List<Regex> ScrubbingPatterns { get; set; } = new();
}
