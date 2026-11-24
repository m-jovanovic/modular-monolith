using System.Security.Claims;
using EquipmentLibrary.Lending.Domain;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.Lending.Loans;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal static class CloseHold
{
    private sealed record LockedHold(HoldSnapshot Snapshot, Guid BorrowerId, HoldPickupWindow PickupWindow);

    internal static Task<IResult> CancelAsync(Guid holdId, ClaimsPrincipal user, LendingContext lending, CancellationToken cancellationToken) =>
        BorrowerIdentity.Read(user) is Guid borrowerId
            ? ExecuteAsync(holdId, borrowerId, lending, cancellationToken)
            : Task.FromResult(LendingErrors.Forbidden());

    internal static Task<IResult> CollectAsync(Guid holdId, LendingContext lending, CancellationToken cancellationToken) =>
        ExecuteAsync(holdId, cancellingBorrower: null, lending, cancellationToken);

    private static async Task<IResult> ExecuteAsync(
        Guid holdId, Guid? cancellingBorrower, LendingContext lending, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await lending.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using NpgsqlCommand find = connection.Sql("""
            SELECT h.copy_id, c.catalog_item_id, c.barcode, h.state, c.item_title, h.borrower_id, h.placed_at
            FROM lending.holds h JOIN lending.copies c ON c.id = h.copy_id
            WHERE h.id = $1 FOR UPDATE OF h
            """, holdId);
        LockedHold? hold = await find.ReadOneAsync(r => new LockedHold(
            new HoldSnapshot(holdId, r.GetGuid(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetString(4)),
            r.GetGuid(5), new HoldPickupWindow(r.GetFieldValue<DateTimeOffset>(6))), cancellationToken);
        if (hold is null)
        {
            return LendingErrors.Problem(404, "hold-not-found", "This hold does not exist.");
        }

        if (cancellingBorrower.HasValue && hold.BorrowerId != cancellingBorrower.Value)
        {
            return LendingErrors.Forbidden();
        }

        if (hold.Snapshot.State != "held")
        {
            return LendingErrors.Problem(409, "hold-not-active", "This hold is already closed.");
        }

        DateTimeOffset now = lending.Clock.GetUtcNow();
        bool isCollection = !cancellingBorrower.HasValue;
        if (isCollection && !hold.PickupWindow.CanCollectAt(now))
        {
            return LendingErrors.Problem(409, "pickup-window-closed", "The pickup window is closed.");
        }

        HoldSnapshot snapshot = hold.Snapshot with { State = isCollection ? "collected" : "cancelled" };
        await using NpgsqlCommand close = connection.Sql(
            "UPDATE lending.holds SET state = $2, closed_at = $3 WHERE id = $1", holdId, snapshot.State, now);
        await close.ExecuteNonQueryAsync(cancellationToken);
        await using NpgsqlCommand setCopy = connection.Sql(
            "UPDATE lending.copies SET state = $2 WHERE id = $1", snapshot.CopyId, isCollection ? "on-loan" : "available");
        await setCopy.ExecuteNonQueryAsync(cancellationToken);
        if (isCollection)
        {
            await using NpgsqlCommand loan = connection.Sql("""
                INSERT INTO lending.loans (id, copy_id, borrower_id, checked_out_at, due_at) VALUES ($1, $2, $3, $4, $5)
                """, holdId, snapshot.CopyId, hold.BorrowerId, now, now + LoanPolicy.LoanPeriod);
            await loan.ExecuteNonQueryAsync(cancellationToken);
        }

        string eventType = isCollection ? HoldLifecycleEvent.Collected : HoldLifecycleEvent.Cancelled;
        await HoldEvents.RecordAsync(connection, snapshot, eventType, 2, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new HoldView(holdId, snapshot.CopyId, snapshot.State, hold.PickupWindow.Deadline, Expired: false));
    }
}
