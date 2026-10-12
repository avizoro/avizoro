using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Avizoro.Server.Data;
using Avizoro.Server.Projects;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Avizoro.Server.Auth;

public static class ReadTokenDefaults
{
    public const string Scheme = "ReadToken";

    /// <summary>Config key holding the shared admin token that gates the read API.</summary>
    public const string ConfigKey = "Auth:ReadToken";

    /// <summary>Claim naming the caller's role: <see cref="RoleAdmin"/> or <see cref="RoleProject"/>.</summary>
    public const string RoleClaim = "avizoro_role";

    /// <summary>Claim naming the single project a scoped caller may read. Absent for admins.</summary>
    public const string ProjectClaim = "avizoro_project";

    public const string RoleAdmin = "admin";
    public const string RoleProject = "project";
}

/// <summary>
/// Authenticates read-side requests two ways:
/// <list type="bullet">
///   <item>the shared <b>admin</b> token (<see cref="ReadTokenDefaults.ConfigKey"/>) — sees every project;</item>
///   <item>a per-project <b>read key</b> — scoped to that one project (matched by hash).</item>
/// </list>
/// The token/key is presented as <c>Authorization: Bearer &lt;value&gt;</c> or, where a
/// header can't be set (SignalR WebSockets, image tags), as an <c>access_token</c>
/// query parameter.
///
/// Fail-open in Development when no admin token is configured (zero-config local
/// runs); fail-closed everywhere else. Note that project read keys work even with
/// no admin token configured, so a production server can be project-scoped only.
/// </summary>
public sealed class ReadTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly string? _configuredToken;
    private readonly bool _isDevelopment;
    private readonly AvizoroDbContext _db;

    public ReadTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration,
        IHostEnvironment environment,
        AvizoroDbContext db)
        : base(options, logger, encoder)
    {
        _configuredToken = configuration[ReadTokenDefaults.ConfigKey];
        _isDevelopment = environment.IsDevelopment();
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ExtractToken();

        // 1) Shared admin token → sees every project.
        if (!string.IsNullOrWhiteSpace(_configuredToken)
            && presented is not null
            && CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(_configuredToken)))
            return AdminSuccess("admin");

        // 2) Per-project read key → scoped to that project.
        if (presented is not null)
        {
            var hash = ProjectKeys.Hash(presented);
            var projectId = await _db.Projects.AsNoTracking()
                .Where(p => p.ReadKeyHash == hash)
                .Select(p => p.ProjectId)
                .FirstOrDefaultAsync();

            if (!string.IsNullOrEmpty(projectId))
                return ProjectSuccess(projectId);
        }

        // 3) Dev-open: no admin token configured and running locally.
        if (string.IsNullOrWhiteSpace(_configuredToken) && _isDevelopment)
            return AdminSuccess("dev");

        // 4) Nothing matched.
        if (presented is null)
            return string.IsNullOrWhiteSpace(_configuredToken)
                ? AuthenticateResult.Fail("Read API is locked: set 'Auth:ReadToken' or use a project read key.")
                : AuthenticateResult.NoResult();

        return AuthenticateResult.Fail("Invalid read credential.");
    }

    private string? ExtractToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var value = header["Bearer ".Length..].Trim();
            if (value.Length > 0)
                return value;
        }

        // WebSockets and <img> requests can't carry an Authorization header.
        var fromQuery = Request.Query["access_token"].ToString();
        return string.IsNullOrWhiteSpace(fromQuery) ? null : fromQuery;
    }

    private AuthenticateResult AdminSuccess(string name)
        => TicketFor(new[]
        {
            new Claim(ClaimTypes.Name, name),
            new Claim(ReadTokenDefaults.RoleClaim, ReadTokenDefaults.RoleAdmin),
        });

    private AuthenticateResult ProjectSuccess(string projectId)
        => TicketFor(new[]
        {
            new Claim(ClaimTypes.Name, projectId),
            new Claim(ReadTokenDefaults.RoleClaim, ReadTokenDefaults.RoleProject),
            new Claim(ReadTokenDefaults.ProjectClaim, projectId),
        });

    private AuthenticateResult TicketFor(IEnumerable<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, ReadTokenDefaults.Scheme);
        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity), ReadTokenDefaults.Scheme);
        return AuthenticateResult.Success(ticket);
    }
}
