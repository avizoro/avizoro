using Avizoro.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Avizoro.Server.Migrations.Sqlite;

/// <summary>
/// Design-time context used only by `dotnet ef` to generate/scaffold this
/// provider's migrations. Never connects at generation time.
/// </summary>
public class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<AvizoroDbContext>
{
    public AvizoroDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AvizoroDbContext>()
            .UseSqlite(
                "Data Source=design.db",
                x => x.MigrationsAssembly(typeof(SqliteDesignTimeFactory).Assembly.GetName().Name))
            .Options;

        return new AvizoroDbContext(options);
    }
}
