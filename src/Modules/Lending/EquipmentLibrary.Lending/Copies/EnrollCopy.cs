using EquipmentLibrary.Catalog.Contracts;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Copies;

internal static class EnrollCopy
{
    internal sealed record Request(Guid CatalogItemId, string Barcode);

    internal static async Task<IResult> HandleAsync(
        Request request, ICatalogLookup catalog, LendingContext lending, CancellationToken cancellationToken)
    {
        string? barcode = request.Barcode?.Trim();
        if (request.CatalogItemId == Guid.Empty || string.IsNullOrEmpty(barcode) || barcode.Length > 80)
        {
            return LendingErrors.Invalid("Provide a catalog item and a barcode of 1 to 80 characters.");
        }

        CatalogItemDescription? item = await catalog.GetItemAsync(request.CatalogItemId, cancellationToken);
        if (item is null)
        {
            return LendingErrors.Problem(404, "catalog-item-not-found", "This catalog item does not exist.");
        }

        await using NpgsqlCommand insert = lending.DataSource.CreateCommand("""
            INSERT INTO lending.copies (id, catalog_item_id, item_title, barcode, state, enrolled_at)
            VALUES ($1, $2, $3, $4, 'available', $5)
            ON CONFLICT (barcode) DO NOTHING
            RETURNING id, catalog_item_id, barcode, state
            """).Bind(Guid.CreateVersion7(), item.Id, item.Title, barcode, lending.Clock.GetUtcNow());
        CopyView? created = await insert.ReadOneAsync(CopyView.Read, cancellationToken);
        if (created is not null)
        {
            return Results.Created($"/api/lending/copies/{created.CopyId}", created);
        }

        // A new statement sees the enrollment that won a concurrent barcode conflict.
        await using NpgsqlCommand find = lending.DataSource.CreateCommand("""
            SELECT id, catalog_item_id, barcode, state FROM lending.copies WHERE barcode = $1
            """).Bind(barcode);
        CopyView? existing = await find.ReadOneAsync(CopyView.Read, cancellationToken);
        return existing?.CatalogItemId == item.Id
            ? Results.Ok(existing)
            : LendingErrors.Problem(409, "barcode-in-use", "Another catalog item already uses this barcode.");
    }
}
