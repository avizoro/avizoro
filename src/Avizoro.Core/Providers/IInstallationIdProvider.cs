namespace Avizoro.Core.Providers;

/// <summary>
/// Supplies a stable, anonymous identifier for this installation of the app, so
/// release health can count affected <em>users</em> without knowing who they are.
/// Platform SDKs persist it (e.g. MAUI Preferences); it is not personal data.
/// </summary>
public interface IInstallationIdProvider
{
    string GetInstallationId();
}

/// <summary>Fallback that keeps one id for the life of the process (not persisted).</summary>
public sealed class ProcessInstallationIdProvider : IInstallationIdProvider
{
    private readonly string _id = Guid.NewGuid().ToString("N");

    public string GetInstallationId() => _id;
}
