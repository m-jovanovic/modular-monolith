using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EquipmentLibrary.Lending.Delivery;

public static class DeliveryModule
{
    public static IServiceCollection AddHoldDelivery(this IServiceCollection services, bool runWorker)
    {
        services.TryAddSingleton<HoldSubscriptions>();
        services.AddSingleton<OutboxStore>();
        services.AddSingleton<IHoldDispatcher, HoldRelay>();
        if (runWorker)
        {
            services.AddHostedService<RelayWorker>();
        }

        return services;
    }
}

internal sealed partial class RelayWorker(IHoldDispatcher relay, ILogger<RelayWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await relay.DeliverNextAsync(stoppingToken) == HoldDispatchOutcome.Idle)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
            }
            catch (NpgsqlException exception)
            {
                StorageUnavailable(logger, exception);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lending storage unavailable during dispatch")]
    private static partial void StorageUnavailable(ILogger logger, Exception exception);
}
