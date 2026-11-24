using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace EquipmentLibrary.Lending;

internal sealed class LendingReadiness([FromKeyedServices("Lending")] NpgsqlDataSource database) : IHealthCheck
{
    private const string Query = """
        SELECT id, catalog_item_id, item_title, barcode, state, enrolled_at FROM lending.copies LIMIT 0;
        SELECT id, copy_id, borrower_id, state, placed_at, closed_at, expired FROM lending.holds LIMIT 0;
        SELECT id, copy_id, borrower_id, checked_out_at, due_at, renewals, returned_at FROM lending.loans LIMIT 0;
        SELECT id, payload, occurred_at, subscribers, eligible_at, lease_id, failure_count,
            last_failure, delivered_at, quarantined_at, trace_parent FROM lending.outbox LIMIT 0;
        SELECT id, message_id, operator_id, redriven_at FROM lending.outbox_redrives LIMIT 0;
        SELECT id, copy_id, reason, opened_at, outcome, resolution, closed_at FROM lending.maintenance_orders LIMIT 0;
        """;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = database.CreateCommand(Query);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
