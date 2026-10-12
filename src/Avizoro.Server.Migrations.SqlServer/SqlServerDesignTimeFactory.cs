using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avizoro.Server.Migrations.SqlServer;

/// <summary>
/// Design-time context used only by `dotnet ef` to generate this provider's
/// migrations. Never connects at generation time.
/// </summary>
public class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<AvizoroDbContext>
{
    public AvizoroDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AvizoroDbContext>()
            .UseSqlServer(
                "Server=localhost;Database=avizoro;Trusted_Connection=True;TrustServerCertificate=True",
                x => x.MigrationsAssembly(typeof(SqlServerDesignTimeFactory).Assembly.GetName().Name))
            .Options;

        return new AvizoroDbContext(options);
    }
}
