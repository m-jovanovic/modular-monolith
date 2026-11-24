using Npgsql;

namespace EquipmentLibrary.Lending.Copies;

internal sealed record CopyView(Guid CopyId, Guid CatalogItemId, string Barcode, string State)
{
    internal static CopyView Read(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3));
}
