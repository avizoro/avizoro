using Microsoft.Data.Sqlite;

namespace Avizoro.Server.Hosting;

/// <summary>
/// Connection-string conveniences for container platforms: PaaS databases (Render,
/// Railway, Heroku-style) hand out <c>postgres://user:pass@host:port/db</c> URLs,
/// which Npgsql doesn't parse, and a SQLite file on a fresh volume needs its
/// directory created first.
/// </summary>
public static class ConnectionStrings
{
    /// <summary>
    /// The PostgreSQL connection string: <c>ConnectionStrings:Events</c>, else the
    /// conventional <c>DATABASE_URL</c>; URL forms are converted to Npgsql's format.
    /// </summary>
    public static string? PostgreSql(IConfiguration config)
    {
        var value = config.GetConnectionString("Events");
        if (string.IsNullOrWhiteSpace(value))
            value = config["DATABASE_URL"];
        return string.IsNullOrWhiteSpace(value) ? null : FromUrl(value);
    }

    /// <summary>Converts <c>postgres(ql)://user:pass@host[:port]/db[?sslmode=…]</c>; other input is returned unchanged.</summary>
    public static string FromUrl(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
            return value;

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var parts = new List<string>
        {
            $"Host={uri.Host}",
            $"Port={(uri.Port > 0 ? uri.Port : 5432)}",
            $"Database={Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'))}",
            $"Username={Uri.UnescapeDataString(userInfo[0])}",
        };
        if (userInfo.Length > 1)
            parts.Add($"Password={Uri.UnescapeDataString(userInfo[1])}");

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
                parts.Add($"SSL Mode={Uri.UnescapeDataString(kv[1])}");
        }

        return string.Join(';', parts);
    }

    /// <summary>
    /// Keeps a database created before the rename: when the configured file is the
    /// default <c>avizoro.db</c> and doesn't exist yet but <c>intelligencekit.db</c>
    /// sits next to it, the old file is used instead of starting empty.
    /// </summary>
    public static string PreferLegacySqliteFile(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            var dataSource = builder.DataSource;
            if (!string.Equals(Path.GetFileName(dataSource), "avizoro.db", StringComparison.OrdinalIgnoreCase) || File.Exists(dataSource))
                return connectionString;

            var legacy = Path.Combine(Path.GetDirectoryName(dataSource) ?? string.Empty, "intelligencekit.db");
            if (!File.Exists(legacy))
                return connectionString;

            builder.DataSource = legacy;
            return builder.ConnectionString;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return connectionString;
        }
    }

    /// <summary>Creates the directory of a file-based SQLite database if it doesn't exist.</summary>
    public static string EnsureSqliteDirectory(string connectionString)
    {
        try
        {
            var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
            if (!string.IsNullOrWhiteSpace(dataSource) && dataSource != ":memory:")
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(dataSource));
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Let SQLite report the real problem when it opens the file.
        }
        return connectionString;
    }
}
