using EquipmentLibrary.Lending.Domain;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.Lending.Persistence;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds.Expiry;

internal sealed record ExpireDueHolds(int Limit)
{
    public const int DefaultLimit = 50;
    public const int MaximumLimit = 100;
}

internal sealed class ExpireDueHoldsHandler(LendingContext lending)
{
    public async Task<int> HandleAsync(ExpireDueHolds command, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(command.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(command.Limit, ExpireDueHolds.MaximumLimit);
        DateTimeOffset now = lending.Clock.GetUtcNow();
        await using NpgsqlConnection connection = await lending.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using NpgsqlCommand expire = connection.Sql("""
            WITH due AS (
                SELECT id FROM lending.holds WHERE state = 'held' AND placed_at <= $1
                ORDER BY placed_at, id LIMIT $2 FOR UPDATE SKIP LOCKED
            ), closed AS (
                UPDATE lending.holds h SET state = 'cancelled', closed_at = $3, expired = true
                FROM due WHERE h.id = due.id RETURNING h.id AS hold_id, h.copy_id
            )
            UPDATE lending.copies c SET state = 'available'
            FROM closed WHERE c.id = closed.copy_id
            RETURNING closed.hold_id, c.id, c.catalog_item_id, c.barcode, c.item_title
            """, HoldPickupWindow.LatestExpiredPlacement(now), command.Limit, now);
        List<HoldSnapshot> snapshots = await expire.ReadListAsync(r =>
            new HoldSnapshot(r.GetGuid(0), r.GetGuid(1), r.GetGuid(2), r.GetString(3), "cancelled", r.GetString(4)), cancellationToken);
        foreach (HoldSnapshot snapshot in snapshots)
        {
            await HoldEvents.RecordAsync(connection, snapshot, HoldLifecycleEvent.Cancelled, 2, now, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return snapshots.Count;
    }
}
