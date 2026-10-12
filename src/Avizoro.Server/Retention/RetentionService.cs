namespace Avizoro.Server.Retention;

/// <summary>
/// Periodically runs the <see cref="RetentionSweeper"/>. Interval comes from
/// <c>Retention:SweepHours</c> (default 6, clamped to at least 1). A sweep runs
/// once at startup and then on that cadence; when retention is disabled each
/// sweep is a cheap no-op, so the service can always be registered.
/// </summary>
public sealed class RetentionService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<RetentionService> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // The sweeper needs the scoped DbContext, so open a scope per run.
                using var scope = _scopeFactory.CreateScope();
                var sweeper = scope.ServiceProvider.GetRequiredService<RetentionSweeper>();
                var result = await sweeper.SweepAsync(stoppingToken);

                if (result.Enabled && result.Total > 0)
                    _logger.LogInformation(
                        "Retention sweep removed {Events} events, {Screenshots} screenshots and {Issues} issues older than {Cutoff:u}.",
                        result.Events, result.Screenshots, result.Issues, result.Cutoff);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed sweep must not take the service (or the app) down.
                _logger.LogError(ex, "Retention sweep failed; will retry next interval.");
            }

            var sweepHours = Math.Max(1, _config.GetValue("Retention:SweepHours", 6));
            try
            {
                await Task.Delay(TimeSpan.FromHours(sweepHours), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
