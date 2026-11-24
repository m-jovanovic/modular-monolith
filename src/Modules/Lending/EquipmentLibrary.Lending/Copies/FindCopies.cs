using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Copies;

internal static class FindCopies
{
    internal static async Task<IResult> HandleAsync(
        Guid? catalogItemId, string? state, int? limit, int? offset, LendingContext lending, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100 || offset is < 0 ||
            state is not (null or "available" or "held" or "on-loan" or "retired" or "maintenance"))
        {
            return LendingErrors.Invalid("Use a supported copy state, nonnegative offset and limit between 1 and 100.");
        }

        await using NpgsqlCommand query = lending.DataSource.CreateCommand("""
            SELECT id, catalog_item_id, barcode, state FROM lending.copies
            WHERE ($1 = FALSE OR catalog_item_id = $2) AND ($3 = '' OR state = $3)
            ORDER BY barcode, id LIMIT $4 OFFSET $5
            """).Bind(catalogItemId.HasValue, catalogItemId.GetValueOrDefault(), state ?? string.Empty, limit ?? 25, offset ?? 0);
        List<CopyView> items = await query.ReadListAsync(CopyView.Read, cancellationToken);
        return Results.Ok(new { items });
    }
}
