using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.Lending.Delivery;

internal sealed record PendingDelivery(Guid Id, Guid Lease, string Payload, string[] Subscribers, int Failures, string? TraceParent);

internal sealed class OutboxStore([FromKeyedServices("Lending")] NpgsqlDataSource database)
{
    internal async Task<PendingDelivery?> ClaimAsync(CancellationToken cancellationToken)
    {
        var lease = Guid.NewGuid();
        await using NpgsqlCommand command = database.CreateCommand("""
            WITH next AS (
                SELECT id FROM lending.outbox
                WHERE delivered_at IS NULL AND quarantined_at IS NULL AND eligible_at <= now()
                ORDER BY eligible_at, occurred_at, id FOR UPDATE SKIP LOCKED LIMIT 1
            )
            UPDATE lending.outbox AS o SET lease_id = $1, eligible_at = now() + interval '30 seconds'
            FROM next WHERE o.id = next.id
            RETURNING o.id, o.payload::text, o.subscribers, o.failure_count, o.trace_parent
            """);
        command.Parameters.AddWithValue(lease);
        await using NpgsqlDataReader row = await command.ExecuteReaderAsync(cancellationToken);
        if (!await row.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PendingDelivery(row.GetGuid(0), lease, row.GetString(1), row.GetFieldValue<string[]>(2),
            row.GetInt32(3), row.IsDBNull(4) ? null : row.GetString(4));
    }

    internal async Task<bool> CompleteAsync(PendingDelivery item, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = database.CreateCommand("""
            UPDATE lending.outbox SET delivered_at = now(), lease_id = NULL, last_failure = NULL
            WHERE id = $1 AND lease_id = $2
            """);
        command.Parameters.AddWithValue(item.Id);
        command.Parameters.AddWithValue(item.Lease);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    internal async Task<bool> FailAsync(PendingDelivery item, Exception failure, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = database.CreateCommand("""
            UPDATE lending.outbox SET failure_count = failure_count + 1, last_failure = $3,
                eligible_at = now() + $4, lease_id = NULL,
                quarantined_at = CASE WHEN failure_count + 1 >= 5 THEN now() END
            WHERE id = $1 AND lease_id = $2
            """);
        command.Parameters.AddWithValue(item.Id);
        command.Parameters.AddWithValue(item.Lease);
        command.Parameters.AddWithValue(failure.GetType().Name);
        command.Parameters.AddWithValue(TimeSpan.FromSeconds(1 << Math.Min(5, item.Failures)));
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
