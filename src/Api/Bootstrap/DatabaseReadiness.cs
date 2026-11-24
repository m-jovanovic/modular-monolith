using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace EquipmentLibrary.Api.Bootstrap;

internal sealed class DatabaseReadiness(IServiceProvider services) : IHealthCheck
{
    // PostgreSQL resolves every column and checks the runtime role's access without reading business rows.
    private static readonly (string Module, string Sql)[] ModuleQueries =
    [
        ("Catalog", """
            SELECT id, title, description, is_archived FROM catalog.items LIMIT 0;
            SELECT id, name FROM catalog.categories LIMIT 0;
            SELECT item_id, category_id FROM catalog.item_categories LIMIT 0;
            """),
        ("Lending", """
            SELECT id, catalog_item_id, item_title, barcode, state, enrolled_at FROM lending.copies LIMIT 0;
            SELECT id, copy_id, borrower_id, state, placed_at, closed_at, expired FROM lending.holds LIMIT 0;
            SELECT id, copy_id, borrower_id, checked_out_at, due_at, renewals, returned_at FROM lending.loans LIMIT 0;
            SELECT id, payload, occurred_at, subscribers, eligible_at, lease_id, failure_count,
                last_failure, delivered_at, quarantined_at, trace_parent FROM lending.outbox LIMIT 0;
            SELECT id, message_id, operator_id, redriven_at FROM lending.outbox_redrives LIMIT 0;
            SELECT id, copy_id, reason, opened_at, outcome, resolution, closed_at FROM lending.maintenance_orders LIMIT 0;
            """),
        ("PickupDesk", """
            SELECT subscriber_id, message_id, payload, received_at, available_at, attempts,
                completed_at, quarantined_at, failure, trace_parent FROM pickup_desk.inbox LIMIT 0;
            SELECT hold_id, copy_id, catalog_item_id, barcode, item_title, hold_state, sequence,
                last_message_id, occurred_at, assigned_to, ready_at, issue FROM pickup_desk.holds LIMIT 0;
            SELECT id, subscriber_id, message_id, operator_id, reason, previous_failure, redriven_at FROM pickup_desk.redrives LIMIT 0;
            """)
    ];

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        foreach ((string module, string sql) in ModuleQueries)
        {
            NpgsqlDataSource database = services.GetRequiredKeyedService<NpgsqlDataSource>(module);
            await using NpgsqlCommand command = database.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return HealthCheckResult.Healthy();
    }
}
