using EquipmentLibrary.Lending.Application.Maintenance;
using EquipmentLibrary.Lending.Domain;
using EquipmentLibrary.Lending.Persistence;
using Npgsql;

namespace EquipmentLibrary.Lending.Maintenance;

internal sealed class PostgresMaintenanceOrders(LendingContext context) : IMaintenanceOrders
{
    public async Task<MaintenanceResult> OpenAsync(Guid copyId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await context.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using NpgsqlCommand claim = connection.Sql(
            "UPDATE lending.copies SET state = 'maintenance' WHERE id = $1 AND state = 'available'", copyId);
        if (await claim.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            await using NpgsqlCommand exists = connection.Sql("SELECT 1 FROM lending.copies WHERE id = $1", copyId);
            return await exists.ExecuteScalarAsync(cancellationToken) is null
                ? new MaintenanceResult.CopyMissing()
                : new MaintenanceResult.CopyUnavailable();
        }

        await using NpgsqlCommand insert = connection.Sql("""
            INSERT INTO lending.maintenance_orders (id, copy_id, reason, opened_at) VALUES ($1, $2, $3, $4)
            RETURNING id, copy_id, reason, opened_at, outcome, resolution, closed_at
            """, Guid.CreateVersion7(), copyId, reason, now);
        MaintenanceOrder order = (await insert.ReadOneAsync(Read, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return new MaintenanceResult.Saved(order);
    }

    public async Task<MaintenanceResult> ResolveAsync(Guid orderId, MaintenanceResolution decision, string note,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await context.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using NpgsqlCommand find = connection.Sql("""
            SELECT id, copy_id, reason, opened_at, outcome, resolution, closed_at
            FROM lending.maintenance_orders WHERE id = $1 FOR UPDATE
            """, orderId);
        MaintenanceOrder? original = await find.ReadOneAsync(Read, cancellationToken);
        if (original is null)
        {
            return new MaintenanceResult.OrderMissing();
        }

        if (original.ClosedAt.HasValue)
        {
            return decision.Matches(original.Outcome, original.Resolution, note)
                ? new MaintenanceResult.Saved(original)
                : new MaintenanceResult.AlreadyResolved();
        }

        await using NpgsqlCommand release = connection.Sql(
            "UPDATE lending.copies SET state = $2 WHERE id = $1 AND state = 'maintenance'", original.CopyId, decision.CopyState);
        if (await release.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException("An open maintenance order must hold its copy out of circulation.");
        }

        await using NpgsqlCommand close = connection.Sql("""
            UPDATE lending.maintenance_orders SET outcome = $2, resolution = $3, closed_at = $4 WHERE id = $1
            RETURNING id, copy_id, reason, opened_at, outcome, resolution, closed_at
            """, orderId, decision.Outcome, note, now);
        MaintenanceOrder order = (await close.ReadOneAsync(Read, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return new MaintenanceResult.Saved(order);
    }

    internal static MaintenanceOrder Read(NpgsqlDataReader row) =>
        new(row.GetGuid(0), row.GetGuid(1), row.GetString(2), row.GetFieldValue<DateTimeOffset>(3),
            row.IsDBNull(4) ? null : row.GetString(4), row.IsDBNull(5) ? null : row.GetString(5),
            row.IsDBNull(6) ? null : row.GetFieldValue<DateTimeOffset>(6));
}
