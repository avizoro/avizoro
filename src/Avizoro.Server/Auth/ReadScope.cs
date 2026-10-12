using System.Security.Claims;

namespace Avizoro.Server.Auth;

/// <summary>Reads the per-project access scope a caller was granted at authentication.</summary>
public static class ReadScope
{
    /// <summary>The single project this caller may read, or <c>null</c> for an admin
    /// (who may read every project).</summary>
    public static string? ProjectScope(this ClaimsPrincipal user)
        => user.FindFirst(ReadTokenDefaults.ProjectClaim)?.Value;

    public static bool IsAdmin(this ClaimsPrincipal user)
        => user.FindFirst(ReadTokenDefaults.RoleClaim)?.Value == ReadTokenDefaults.RoleAdmin;
}
