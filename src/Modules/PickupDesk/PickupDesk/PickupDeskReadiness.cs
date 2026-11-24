using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace EquipmentLibrary.PickupDesk;

internal sealed class PickupDeskReadiness([FromKeyedServices("PickupDesk")] NpgsqlDataSource database) : IHealthCheck
{
    private const string Query = """
        SELECT subscriber_id, message_id, payload, received_at, available_at, attempts,
            completed_at, quarantined_at, failure, trace_parent FROM pickup_desk.inbox LIMIT 0;
        SELECT hold_id, copy_id, catalog_item_id, barcode, item_title, hold_state, sequence,
            last_message_id, occurred_at, assigned_to, ready_at, issue FROM pickup_desk.holds LIMIT 0;
        SELECT id, subscriber_id, message_id, operator_id, reason, previous_failure, redriven_at FROM pickup_desk.redrives LIMIT 0;
        """;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = database.CreateCommand(Query);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
