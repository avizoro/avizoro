using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avizoro.Server.Migrations.PostgreSql;

/// <summary>
/// Design-time context used only by `dotnet ef` to generate this provider's
/// migrations. Never connects at generation time.
/// </summary>
public class PostgreSqlDesignTimeFactory : IDesignTimeDbContextFactory<AvizoroDbContext>
{
    public AvizoroDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AvizoroDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=avizoro;Username=postgres",
                x => x.MigrationsAssembly(typeof(PostgreSqlDesignTimeFactory).Assembly.GetName().Name))
            .Options;

        return new AvizoroDbContext(options);
    }
}
