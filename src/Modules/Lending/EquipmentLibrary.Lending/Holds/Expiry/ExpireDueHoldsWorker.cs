using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds.Expiry;

internal sealed partial class ExpireDueHoldsWorker(
    IServiceScopeFactory scopeFactory, TimeProvider clock, ILogger<ExpireDueHoldsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                ExpireDueHoldsHandler handler = scope.ServiceProvider.GetRequiredService<ExpireDueHoldsHandler>();
                int expired = await handler.HandleAsync(new ExpireDueHolds(ExpireDueHolds.DefaultLimit), stoppingToken);
                if (expired > 0)
                {
                    LogExpired(logger, expired);
                }
            }
            catch (Exception exception) when (exception is NpgsqlException or TimeoutException)
            {
                LogDatabaseFailure(logger, exception);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Released {Count} expired pickup holds.")]
    private static partial void LogExpired(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Hold expiry could not reach storage. The next tick will retry.")]
    private static partial void LogDatabaseFailure(ILogger logger, Exception exception);
}
