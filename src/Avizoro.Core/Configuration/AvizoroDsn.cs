namespace Avizoro.Core.Configuration;

/// <summary>
/// Parses an Avizoro DSN into its parts. The DSN bundles the server
/// location and the project identity into a single connection string, e.g.
/// <c>http://{projectKey}@{host}:{port}/{projectId}</c>.
///
/// The project key is a PUBLIC identifier (it ships inside the client app), not
/// a secret. It exists to route/separate projects on a shared self-hosted
/// server, not to authenticate the user.
/// </summary>
public sealed class AvizoroDsn
{
    public string ServerUrl { get; }

    public string ProjectKey { get; }

    public string ProjectId { get; }

    private AvizoroDsn(string serverUrl, string projectKey, string projectId)
    {
        ServerUrl = serverUrl;
        ProjectKey = projectKey;
        ProjectId = projectId;
    }

    public static AvizoroDsn Parse(string dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn))
            throw new ArgumentException("The Avizoro DSN must not be empty.", nameof(dsn));

        if (!Uri.TryCreate(dsn, UriKind.Absolute, out var uri))
            throw new FormatException($"Invalid Avizoro DSN: '{dsn}'. Expected 'http(s)://key@host:port/projectId'.");

        // Uri.Authority is host[:port] without the user-info and without the
        // default port, which is exactly the server base we want.
        var serverUrl = $"{uri.Scheme}://{uri.Authority}";
        var projectKey = uri.UserInfo;
        var projectId = uri.AbsolutePath.Trim('/');

        if (string.IsNullOrEmpty(projectId))
            throw new FormatException($"Invalid Avizoro DSN: '{dsn}' is missing the project id path segment.");

        return new AvizoroDsn(serverUrl, projectKey, projectId);
    }
}
