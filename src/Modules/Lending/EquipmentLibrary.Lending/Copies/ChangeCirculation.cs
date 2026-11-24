using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Copies;

internal static class ChangeCirculation
{
    internal static Task<IResult> RetireAsync(Guid copyId, LendingContext lending, CancellationToken cancellationToken) =>
        ChangeAsync(copyId, "available", "retired", lending, cancellationToken);

    internal static Task<IResult> ReinstateAsync(Guid copyId, LendingContext lending, CancellationToken cancellationToken) =>
        ChangeAsync(copyId, "retired", "available", lending, cancellationToken);

    private static async Task<IResult> ChangeAsync(
        Guid copyId, string from, string to, LendingContext lending, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand change = lending.DataSource.CreateCommand("""
            UPDATE lending.copies SET state = $3 WHERE id = $1 AND state = $2
            RETURNING id, catalog_item_id, barcode, state
            """).Bind(copyId, from, to);
        CopyView? changed = await change.ReadOneAsync(CopyView.Read, cancellationToken);
        if (changed is not null)
        {
            return Results.Ok(changed);
        }

        await using NpgsqlCommand read = lending.DataSource.CreateCommand("""
            SELECT id, catalog_item_id, barcode, state FROM lending.copies WHERE id = $1
            """).Bind(copyId);
        CopyView? current = await read.ReadOneAsync(CopyView.Read, cancellationToken);
        if (current is null)
        {
            return LendingErrors.CopyNotFound();
        }

        return current.State == to ? Results.Ok(current)
            : LendingErrors.Problem(409, "copy-in-use", "The current copy state prevents this circulation change.");
    }
}
