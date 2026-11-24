using System.Security.Claims;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal static class PlaceHold
{
    internal sealed record Request(Guid CopyId);

    internal static async Task<IResult> HandleAsync(
        Request request, ClaimsPrincipal user, LendingContext lending, CancellationToken cancellationToken)
    {
        if (request.CopyId == Guid.Empty)
        {
            return LendingErrors.Invalid("Provide the physical copy ID.");
        }

        if (BorrowerIdentity.Read(user) is not Guid borrowerId)
        {
            return LendingErrors.Forbidden();
        }

        await using NpgsqlConnection connection = await lending.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        var holdId = Guid.CreateVersion7();
        DateTimeOffset now = lending.Clock.GetUtcNow();

        // PostgreSQL rechecks the state after a competing UPDATE commits.
        await using NpgsqlCommand claim = connection.Sql("""
            UPDATE lending.copies SET state = 'held' WHERE id = $1 AND state = 'available'
            RETURNING catalog_item_id, barcode, item_title
            """, request.CopyId);
        HoldSnapshot? snapshot = await claim.ReadOneAsync(r => new HoldSnapshot(
            holdId, request.CopyId, r.GetGuid(0), r.GetString(1), "held", r.GetString(2)), cancellationToken);
        if (snapshot is null)
        {
            await using NpgsqlCommand exists = connection.Sql("SELECT 1 FROM lending.copies WHERE id = $1", request.CopyId);
            return await exists.ExecuteScalarAsync(cancellationToken) is null
                ? LendingErrors.CopyNotFound()
                : LendingErrors.Problem(409, "copy-unavailable", "This copy is unavailable.");
        }

        await using NpgsqlCommand insert = connection.Sql("""
            INSERT INTO lending.holds (id, copy_id, borrower_id, state, placed_at) VALUES ($1, $2, $3, 'held', $4)
            """, holdId, request.CopyId, borrowerId, now);
        await insert.ExecuteNonQueryAsync(cancellationToken);
        await HoldEvents.RecordAsync(connection, snapshot, HoldLifecycleEvent.Placed, 1, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Created($"/api/lending/holds/{holdId}", new HoldView(holdId, request.CopyId, "held"));
    }
}
