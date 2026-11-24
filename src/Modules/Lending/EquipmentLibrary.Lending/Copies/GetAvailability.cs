using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Copies;

internal static class GetAvailability
{
    internal sealed record Availability(Guid CatalogItemId, int Available, int Held, int OnLoan, int Retired);

    internal static async Task<IResult> HandleAsync(Guid catalogItemId, LendingContext lending, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand query = lending.DataSource.CreateCommand("""
            SELECT count(*) FILTER (WHERE state = 'available')::integer, count(*) FILTER (WHERE state = 'held')::integer,
                   count(*) FILTER (WHERE state = 'on-loan')::integer, count(*) FILTER (WHERE state = 'retired')::integer
            FROM lending.copies WHERE catalog_item_id = $1
            """).Bind(catalogItemId);
        Availability? result = await query.ReadOneAsync(
            r => new Availability(catalogItemId, r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)), cancellationToken);
        return Results.Ok(result);
    }
}
