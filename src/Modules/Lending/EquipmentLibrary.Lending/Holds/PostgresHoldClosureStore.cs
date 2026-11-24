using EquipmentLibrary.Lending.Application.Holds.CloseHold;
using EquipmentLibrary.Lending.Domain;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.Lending.Loans;
using EquipmentLibrary.Lending.Persistence;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal sealed class PostgresHoldClosureStore(LendingContext lending) : IHoldClosureStore
{
    private sealed record LockedHold(Guid CopyId, Guid CatalogItemId, string Barcode, string ItemTitle, HoldForClosure Hold);

    public async Task<CloseHoldResult> TryCloseAsync(
        Guid holdId, Func<HoldForClosure, DateTimeOffset, HoldClosureDecision> decide, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await lending.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        LockedHold? locked = await ReadLockedAsync(connection, holdId, cancellationToken);
        if (locked is null)
        {
            return new CloseHoldResult.MissingHold();
        }

        DateTimeOffset now = lending.Clock.GetUtcNow();
        HoldClosureDecision decision = decide(locked.Hold, now);
        if (decision is HoldClosureDecision.Refused refusal)
        {
            return new CloseHoldResult.Refused(refusal.Reason);
        }

        var approved = (HoldClosureDecision.Approved)decision;
        bool collected = approved.Action == HoldClosureAction.Collect;
        string state = collected ? "collected" : "cancelled";
        await using NpgsqlCommand close = connection.Sql(
            "UPDATE lending.holds SET state = $2, closed_at = $3 WHERE id = $1", holdId, state, now);
        await close.ExecuteNonQueryAsync(cancellationToken);
        await using NpgsqlCommand setCopy = connection.Sql(
            "UPDATE lending.copies SET state = $2 WHERE id = $1", locked.CopyId, collected ? "on-loan" : "available");
        await setCopy.ExecuteNonQueryAsync(cancellationToken);
        if (collected)
        {
            await using NpgsqlCommand loan = connection.Sql("""
                INSERT INTO lending.loans (id, copy_id, borrower_id, checked_out_at, due_at) VALUES ($1, $2, $3, $4, $5)
                """, holdId, locked.CopyId, locked.Hold.BorrowerId, now, now + LoanPolicy.LoanPeriod);
            await loan.ExecuteNonQueryAsync(cancellationToken);
        }

        var snapshot = new HoldSnapshot(holdId, locked.CopyId, locked.CatalogItemId, locked.Barcode, state, locked.ItemTitle);
        string eventType = collected ? HoldLifecycleEvent.Collected : HoldLifecycleEvent.Cancelled;
        await HoldEvents.RecordAsync(connection, lending.Subscriptions, snapshot, eventType, 2, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CloseHoldResult.Closed(holdId, locked.CopyId, state, locked.Hold.Window);
    }

    private static async Task<LockedHold?> ReadLockedAsync(NpgsqlConnection connection, Guid holdId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand find = connection.Sql("""
            SELECT h.copy_id, c.catalog_item_id, c.barcode, c.item_title, h.borrower_id, h.state, h.placed_at
            FROM lending.holds h JOIN lending.copies c ON c.id = h.copy_id
            WHERE h.id = $1 FOR UPDATE OF h
            """, holdId);
        return await find.ReadOneAsync(r => new LockedHold(r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3),
            new HoldForClosure(r.GetGuid(4), r.GetString(5), new HoldPickupWindow(r.GetFieldValue<DateTimeOffset>(6)))), cancellationToken);
    }
}
