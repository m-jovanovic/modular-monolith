using EquipmentLibrary.Lending.Domain;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal sealed record HoldView(Guid HoldId, Guid CopyId, string State, DateTimeOffset PickupDeadline, bool Expired)
{
    internal static HoldView Read(NpgsqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
            new HoldPickupWindow(reader.GetFieldValue<DateTimeOffset>(3)).Deadline, reader.GetBoolean(4));
}
