using EquipmentLibrary.Lending.Application.Holds.PlaceHold;
using EquipmentLibrary.Lending.Domain;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.Lending.Persistence;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal sealed class PostgresHoldReservationStore(LendingContext context) : IHoldReservationStore
{
    public async Task<PlaceHoldResult> TryPlaceAsync(
        PlaceHoldCommand command, Guid holdId, DateTimeOffset placedAt, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await context.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        // PostgreSQL rechecks the state after a competing UPDATE commits.
        await using NpgsqlCommand claim = connection.Sql("""
            UPDATE lending.copies SET state = 'held' WHERE id = $1 AND state = 'available'
            RETURNING catalog_item_id, barcode, item_title
            """, command.CopyId);
        HoldSnapshot? snapshot = await claim.ReadOneAsync(r => new HoldSnapshot(
            holdId, command.CopyId, r.GetGuid(0), r.GetString(1), "held", r.GetString(2)), cancellationToken);
        if (snapshot is null)
        {
            await using NpgsqlCommand exists = connection.Sql("SELECT 1 FROM lending.copies WHERE id = $1", command.CopyId);
            return await exists.ExecuteScalarAsync(cancellationToken) is null
                ? new PlaceHoldResult.MissingCopy()
                : new PlaceHoldResult.Unavailable();
        }

        await using NpgsqlCommand insert = connection.Sql("""
            INSERT INTO lending.holds (id, copy_id, borrower_id, state, placed_at) VALUES ($1, $2, $3, 'held', $4) RETURNING placed_at
            """, holdId, command.CopyId, command.BorrowerId, placedAt);
        var storedAt = (DateTime)(await insert.ExecuteScalarAsync(cancellationToken))!;
        var window = new HoldPickupWindow(storedAt);
        await HoldEvents.RecordAsync(connection, context.Subscriptions, snapshot, HoldLifecycleEvent.Placed, 1, placedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PlaceHoldResult.Placed(holdId, command.CopyId, window);
    }
}
