using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Copies;

internal static class GetCopy
{
    internal static async Task<IResult> HandleAsync(Guid copyId, LendingContext lending, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = lending.DataSource.CreateCommand("""
            SELECT id, catalog_item_id, barcode, state FROM lending.copies WHERE id = $1
            """).Bind(copyId);
        CopyView? copy = await command.ReadOneAsync(CopyView.Read, cancellationToken);
        return copy is null ? LendingErrors.CopyNotFound() : Results.Ok(copy);
    }
}
