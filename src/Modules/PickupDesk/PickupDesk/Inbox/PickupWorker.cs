using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace EquipmentLibrary.PickupDesk.Inbox;

internal sealed partial class PickupWorker(ProcessPickupInbox inbox, ILogger<PickupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await inbox.ProcessNextAsync(stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), stoppingToken);
                }
            }
            catch (NpgsqlException ex)
            {
                DatabaseUnavailable(logger, ex);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pickup inbox processing will retry after a database error.")]
    private static partial void DatabaseUnavailable(ILogger logger, Exception exception);
}
