using EquipmentLibrary.Catalog.Contracts;

namespace EquipmentLibrary.Catalog.Items;

internal sealed class CatalogLookup([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database) : ICatalogLookup
{
    public async Task<CatalogItemDescription?> GetItemAsync(Guid itemId, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand query = database.CreateCommand(
            "SELECT id, title, description FROM catalog.items WHERE id = $1");
        query.Parameters.AddWithValue(itemId);
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CatalogItemDescription(reader.GetGuid(0), reader.GetString(1), reader.GetString(2))
            : null;
    }
}
