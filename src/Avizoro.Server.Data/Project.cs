namespace Avizoro.Server.Data;

/// <summary>
/// A registered project (tenant). It ties the public DSN identifiers used at
/// ingest (<see cref="ProjectId"/> in the path, <see cref="ProjectKey"/> in the
/// user-info) to a secret <see cref="ReadKeyHash"/> that scopes read access to
/// just this project's data. The read key itself is never stored — only its
/// hash — so it's shown once at creation/rotation and can't be recovered.
/// </summary>
public class Project
{
    public Guid Id { get; set; }

    /// <summary>Public routing id from the DSN path; events carry it. Unique.</summary>
    public string ProjectId { get; set; } = string.Empty;

    /// <summary>Public ingest key from the DSN user-info. Not a secret.</summary>
    public string ProjectKey { get; set; } = string.Empty;

    /// <summary>Human-friendly name for the dashboard/admin.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) of the project's read key. The key gates reads for
    /// this project only.</summary>
    public string ReadKeyHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
