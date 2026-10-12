using Avizoro.Core.Configuration;
using Avizoro.Core.Models;
using Avizoro.Core.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace Avizoro.Maui.CrashReporting;

/// <summary>
/// On the launch after a crash, asks the user what they were doing (opt-in via
/// <see cref="AvizoroOptions.EnableCrashFeedbackPrompt"/>) and sends the
/// answer linked to that crash. The crash reporter remembers the crash's event id
/// in Preferences; this asks at most once per crash.
/// </summary>
public sealed class CrashFeedbackPrompt
{
    internal const string PendingKey = "avizoro.pending_feedback_event";

    private readonly IAvizoro _kit;
    private readonly AvizoroOptions _options;

    public CrashFeedbackPrompt(IAvizoro kit, AvizoroOptions options)
    {
        _kit = kit;
        _options = options;
    }

    /// <summary>Called from the crash handler (after the crash was persisted).</summary>
    internal static void RememberCrash(Guid? eventId)
    {
        try
        {
            if (eventId is { } id)
                Preferences.Default.Set(PendingKey, id.ToString());
        }
        catch
        {
            // Crash handler: never throw.
        }
    }

    public void Start()
    {
        string? pending;
        try
        {
            pending = Preferences.Default.Get<string?>(PendingKey, null);
            if (pending is not null)
                Preferences.Default.Remove(PendingKey); // ask at most once
        }
        catch
        {
            return;
        }

        if (_options.EnableCrashFeedbackPrompt && Guid.TryParse(pending, out var crashId))
            _ = AskAsync(crashId);
    }

    private async Task AskAsync(Guid crashId)
    {
        try
        {
            // Wait for the first page to be on screen.
            Page? page = null;
            for (var i = 0; i < 100 && page is null; i++)
            {
                page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is null)
                    await Task.Delay(200);
            }
            if (page is null)
                return;

            await Task.Delay(500); // let the first frame settle

            var comments = await MainThread.InvokeOnMainThreadAsync(() => page.DisplayPromptAsync(
                _options.CrashFeedbackTitle, _options.CrashFeedbackMessage,
                _options.CrashFeedbackAccept, _options.CrashFeedbackCancel,
                maxLength: 1000, keyboard: Keyboard.Text));

            if (!string.IsNullOrWhiteSpace(comments))
                await _kit.CaptureFeedbackAsync(new UserFeedback { EventId = crashId, Comments = comments });
        }
        catch
        {
            // Feedback is optional; never disturb the app.
        }
    }
}
