using Npgsql;

namespace EquipmentLibrary.PickupDesk.Queries;

internal sealed record PickupList(IReadOnlyList<PickupView> Items);

internal sealed record PickupView(
    Guid HoldId, Guid CopyId, Guid CatalogItemId, string Barcode, string? ItemTitle,
    string State, DateTime OccurredAt, Guid? AssignedTo, DateTime? ReadyAt, string? Issue)
{
    internal const string Columns =
        "hold_id, copy_id, catalog_item_id, barcode, item_title, hold_state, occurred_at, assigned_to, ready_at, issue";

    internal static PickupView Read(NpgsqlDataReader row) =>
        new(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2), row.GetString(3),
            row.IsDBNull(4) ? null : row.GetString(4), row.GetString(5), row.GetDateTime(6),
            row.IsDBNull(7) ? null : row.GetGuid(7), row.IsDBNull(8) ? null : row.GetDateTime(8),
            row.IsDBNull(9) ? null : row.GetString(9));
}
