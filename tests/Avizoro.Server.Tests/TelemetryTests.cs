using System.Net;
using System.Net.Http.Json;

namespace Avizoro.Server.Tests;

public class TelemetryTests : IClassFixture<ServerAppFactory>
{
    private readonly ServerAppFactory _factory;

    public TelemetryTests(ServerAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task HealthProbes_AreHealthy_WithoutAuth(string path)
    {
        var response = await _factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Metrics_RequireAdmin_AndExposeIngestCounters()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/metrics")).StatusCode);

        await _factory.CreateClient().PostAsJsonAsync("/events", TestEvents.Exception(projectId: "metrics-project"));

        // The Prometheus exporter caches a scrape briefly; retry until the counter shows.
        var authed = _factory.CreateAuthorizedClient();
        string body = "";
        for (var i = 0; i < 20 && !body.Contains("metrics-project"); i++)
        {
            body = await authed.GetStringAsync("/metrics");
            if (!body.Contains("metrics-project"))
                await Task.Delay(250);
        }

        Assert.Contains("avizoro_events_ingested", body);
        Assert.Contains("project=\"metrics-project\"", body);
        Assert.Contains("avizoro_ingest_duration", body);
    }
}

public class ConnectionStringTests
{
    [Theory]
    [InlineData("postgres://ik:p%40ss@db.example.com:6543/intelligence?sslmode=require",
        "Host=db.example.com;Port=6543;Database=intelligence;Username=ik;Password=p@ss;SSL Mode=require")]
    [InlineData("postgresql://ik@db/ik", "Host=db;Port=5432;Database=ik;Username=ik")]
    [InlineData("Host=db;Database=ik", "Host=db;Database=ik")]
    public void PostgresUrls_AreConverted(string input, string expected)
        => Assert.Equal(expected, Avizoro.Server.Hosting.ConnectionStrings.FromUrl(input));

    [Fact]
    public void SqliteDirectory_IsCreated()
    {
        var dir = Path.Combine(Path.GetTempPath(), "avz-sqlite-" + Guid.NewGuid().ToString("N"), "data");
        Avizoro.Server.Hosting.ConnectionStrings.EnsureSqliteDirectory($"Data Source={Path.Combine(dir, "avz.db")}");
        Assert.True(Directory.Exists(dir));
    }

    [Fact]
    public void LegacySqliteFile_IsKept_WhenTheDefaultDoesNotExistYet()
    {
        var dir = Directory.CreateTempSubdirectory("avz-legacy-").FullName;
        var legacy = Path.Combine(dir, "intelligencekit.db");
        File.WriteAllText(legacy, "");

        var result = Avizoro.Server.Hosting.ConnectionStrings.PreferLegacySqliteFile($"Data Source={Path.Combine(dir, "avizoro.db")}");

        Assert.Equal(legacy, new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(result).DataSource);
    }

    [Fact]
    public void LegacySqliteFile_IsIgnored_ForOtherNamesOrAnExistingDatabase()
    {
        var dir = Directory.CreateTempSubdirectory("avz-legacy-").FullName;
        File.WriteAllText(Path.Combine(dir, "intelligencekit.db"), "");

        var custom = $"Data Source={Path.Combine(dir, "events.db")}";
        Assert.Equal(custom, Avizoro.Server.Hosting.ConnectionStrings.PreferLegacySqliteFile(custom));

        var current = $"Data Source={Path.Combine(dir, "avizoro.db")}";
        File.WriteAllText(Path.Combine(dir, "avizoro.db"), "");
        Assert.Equal(current, Avizoro.Server.Hosting.ConnectionStrings.PreferLegacySqliteFile(current));
    }
}
