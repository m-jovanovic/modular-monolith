using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal sealed record HoldView(Guid HoldId, Guid CopyId, string State)
{
    internal static HoldView Read(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2));
}
