using Avizoro.Core.Providers;

namespace Avizoro.Maui.Providers;

/// <summary>
/// Anonymous installation id kept in app Preferences: generated on first use,
/// stable across launches, reset only when the app is reinstalled or its data
/// cleared. Used to count affected users for release health.
/// </summary>
public sealed class MauiInstallationIdProvider : IInstallationIdProvider
{
    private const string Key = "avizoro.installation_id";

    // Key used by 1.x (IntelligenceKit): read once so upgraded installs keep their id.
    private const string LegacyKey = "intelligencekit.installation_id";
    private string? _cached;

    public string GetInstallationId()
    {
        if (_cached is not null)
            return _cached;

        var id = Preferences.Default.Get<string?>(Key, null);
        if (string.IsNullOrEmpty(id))
        {
            id = Preferences.Default.Get<string?>(LegacyKey, null);
            if (string.IsNullOrEmpty(id))
                id = Guid.NewGuid().ToString("N");
            Preferences.Default.Set(Key, id);
        }

        return _cached = id;
    }
}
