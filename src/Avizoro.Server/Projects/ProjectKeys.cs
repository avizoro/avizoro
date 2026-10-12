using System.Security.Cryptography;

namespace Avizoro.Server.Projects;

/// <summary>Generates and hashes project keys.</summary>
public static class ProjectKeys
{
    /// <summary>A high-entropy read key, e.g. <c>avzr_a1b2...</c> (URL-safe).</summary>
    public static string NewReadKey() => "avzr_" + RandomToken();

    /// <summary>A public ingest/routing key, e.g. <c>avzp_a1b2...</c> (URL-safe).</summary>
    public static string NewProjectKey() => "avzp_" + RandomToken();

    /// <summary>Stable SHA-256 (lowercase hex) of a key, for storage/lookup.</summary>
    public static string Hash(string key)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))
            .ToLowerInvariant();

    private static string RandomToken()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        // URL-safe base64 without padding.
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
