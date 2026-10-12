using Avizoro.Server.Data;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Avizoro.Server.Telemetry;

/// <summary>Readiness: the database answers. (Liveness has no checks — the process is up.)</summary>
public sealed class DatabaseHealthCheck(AvizoroDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            return await db.Database.CanConnectAsync(cancellationToken)
                ? HealthCheckResult.Healthy("Database reachable.")
                : HealthCheckResult.Unhealthy("Database not reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database check failed.", ex);
        }
    }
}

public static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    /// <summary>
    /// <c>/health/live</c> (process up) and <c>/health/ready</c> (database reachable),
    /// unauthenticated so orchestrators and load balancers can probe them.
    /// </summary>
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains(ReadyTag) });
    }
}
