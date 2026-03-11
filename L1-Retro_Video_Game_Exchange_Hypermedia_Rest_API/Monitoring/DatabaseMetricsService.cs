using System.Data;
using L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Data;
using Microsoft.EntityFrameworkCore;
using Prometheus;

namespace L1_Retro_Video_Game_Exchange_Hypermedia_Rest_API.Monitoring;

public sealed class DatabaseMetricsService : BackgroundService
{
    private static readonly Gauge DatabaseUp = Metrics.CreateGauge(
        "database_up",
        "Whether the API can connect to the database (1 = up, 0 = down).");

    private readonly IServiceProvider _services;
    private readonly ILogger<DatabaseMetricsService> _logger;

    public DatabaseMetricsService(IServiceProvider services, ILogger<DatabaseMetricsService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ExchangeDbContext>();

            try
            {
                await db.Database.OpenConnectionAsync(stoppingToken);
                DatabaseUp.Set(1);
            }
            catch (Exception ex)
            {
                DatabaseUp.Set(0);
                _logger.LogWarning(ex, "Database connectivity check failed.");
            }
            finally
            {
                if (db.Database.GetDbConnection().State != ConnectionState.Closed)
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }
    }
}