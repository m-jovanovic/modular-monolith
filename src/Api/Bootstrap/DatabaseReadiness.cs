using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace EquipmentLibrary.Api.Bootstrap;

internal sealed class DatabaseReadiness(IServiceProvider services) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        foreach ((string module, string sql) in new[]
        {
            ("Catalog", "SELECT id FROM catalog.items LIMIT 0"),
            ("Lending", "SELECT trace_parent FROM lending.outbox LIMIT 0"),
            ("PickupDesk", "SELECT message_id FROM pickup_desk.inbox LIMIT 0")
        })
        {
            NpgsqlDataSource database = services.GetRequiredKeyedService<NpgsqlDataSource>(module);
            await using NpgsqlCommand command = database.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return HealthCheckResult.Healthy();
    }
}
