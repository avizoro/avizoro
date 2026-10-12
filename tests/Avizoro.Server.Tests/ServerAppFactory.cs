using System.Net.Http.Headers;
using Avizoro.Server.Data;
using Avizoro.Server.Retention;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Avizoro.Server.Tests;

/// <summary>
/// Hosts the real Server app for integration tests, but swaps its file-backed
/// SQLite database for a single shared in-memory connection (kept open for the
/// factory's lifetime so the schema survives between requests) and pins a known
/// read token so the auth matrix is deterministic.
/// </summary>
public sealed class ServerAppFactory : WebApplicationFactory<Program>
{
    public const string TestToken = "test-admin-token";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly string _environment;
    private readonly bool _withToken;
    private readonly IReadOnlyDictionary<string, string?>? _extraSettings;
    private readonly bool _disableHostedServices;
    private readonly Action<IServiceCollection>? _configureServices;

    // xUnit's IClassFixture requires exactly one public constructor; it yields the
    // default configuration (Development + a configured read token). Other
    // configurations are created explicitly via the Create* factory methods.
    public ServerAppFactory() : this("Development", withToken: true)
    {
    }

    private ServerAppFactory(
        string environment,
        bool withToken,
        IReadOnlyDictionary<string, string?>? extraSettings = null,
        bool disableHostedServices = false,
        Action<IServiceCollection>? configureServices = null)
    {
        _environment = environment;
        _withToken = withToken;
        _extraSettings = extraSettings;
        _disableHostedServices = disableHostedServices;
        _configureServices = configureServices;
        _connection.Open();
    }

    /// <summary>A factory with an explicit environment / token configuration,
    /// for tests that exercise the fail-open vs fail-closed auth matrix.</summary>
    public static ServerAppFactory Create(string environment, bool withToken)
        => new(environment, withToken);

    /// <summary>A factory with retention enabled at <paramref name="days"/> and the
    /// timed background sweeper removed, so tests drive <see cref="RetentionSweeper"/>
    /// deterministically instead of racing the hosted service.</summary>
    public static ServerAppFactory CreateWithRetention(int days)
        => new("Development", withToken: true,
            new Dictionary<string, string?>
            {
                ["Retention:Enabled"] = "true",
                ["Retention:Days"] = days.ToString(),
            },
            disableHostedServices: true);

    /// <summary>A factory with retention explicitly disabled (and the background
    /// sweeper removed), for asserting the no-op path.</summary>
    public static ServerAppFactory CreateWithRetentionDisabled()
        => new("Development", withToken: true,
            new Dictionary<string, string?> { ["Retention:Enabled"] = "false" },
            disableHostedServices: true);

    /// <summary>A factory with a tiny ingest rate limit, for exercising 429s
    /// without having to fire hundreds of requests.</summary>
    public static ServerAppFactory CreateWithIngestLimit(int permitLimit, int windowSeconds)
        => new("Development", withToken: true, new Dictionary<string, string?>
        {
            ["RateLimit:Ingest:Enabled"] = "true",
            ["RateLimit:Ingest:PermitLimit"] = permitLimit.ToString(),
            ["RateLimit:Ingest:WindowSeconds"] = windowSeconds.ToString(),
        });

    /// <summary>A factory with ingest rate limiting turned off.</summary>
    public static ServerAppFactory CreateWithIngestLimitDisabled()
        => new("Development", withToken: true, new Dictionary<string, string?>
        {
            ["RateLimit:Ingest:Enabled"] = "false",
        });

    /// <summary>A factory that enforces project registration on ingest, for the
    /// unknown-project rejection tests.</summary>
    public static ServerAppFactory CreateWithIngestValidation()
        => new("Development", withToken: true, new Dictionary<string, string?>
        {
            ["Ingest:RequireKnownProject"] = "true",
        });

    /// <summary>A factory with extra settings and/or service overrides (e.g. a fake
    /// outbound HTTP handler), applied after the app's own registrations.</summary>
    public static ServerAppFactory CreateWith(
        Action<IServiceCollection>? configureServices = null,
        IReadOnlyDictionary<string, string?>? settings = null)
        => new("Development", withToken: true, settings, configureServices: configureServices);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                // Existing tests post events without registering projects, so ingest
                // validation is off by default here; the validation tests re-enable it.
                ["Ingest:RequireKnownProject"] = "false",
            };
            if (_withToken)
                settings["Auth:ReadToken"] = TestToken;

            if (_extraSettings is not null)
            {
                foreach (var kv in _extraSettings)
                    settings[kv.Key] = kv.Value;
            }

            // Added last, so it wins over the app's appsettings.json.
            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureServices(services =>
        {
            // Replace the DbContext registration (Program wired it to a file) with
            // one bound to our shared in-memory connection. Migrations still run on
            // startup against it via the Sqlite migrations assembly.
            RemoveAll(services, typeof(DbContextOptions<AvizoroDbContext>));
            RemoveAll(services, typeof(AvizoroDbContext));

            services.AddDbContext<AvizoroDbContext>(options =>
                options.UseSqlite(
                    _connection,
                    x => x.MigrationsAssembly("Avizoro.Server.Migrations.Sqlite")));

            // Drop background hosted services (the retention sweeper) so tests can
            // drive their logic directly without a timer racing them.
            if (_disableHostedServices)
                RemoveAll(services, typeof(IHostedService));

            _configureServices?.Invoke(services);
        });
    }

    private static void RemoveAll(IServiceCollection services, Type serviceType)
    {
        for (var i = services.Count - 1; i >= 0; i--)
        {
            if (services[i].ServiceType == serviceType)
                services.RemoveAt(i);
        }
    }

    /// <summary>An HttpClient carrying the admin read token (passes read-side auth).</summary>
    public HttpClient CreateAuthorizedClient() => CreateClientWithToken(TestToken);

    /// <summary>An HttpClient carrying an arbitrary bearer token (e.g. a project read key).</summary>
    public HttpClient CreateClientWithToken(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }
}
