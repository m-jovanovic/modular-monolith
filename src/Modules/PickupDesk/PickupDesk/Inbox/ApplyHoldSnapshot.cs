using EquipmentLibrary.Lending.IntegrationEvents;
using Npgsql;
using NpgsqlTypes;

namespace EquipmentLibrary.PickupDesk.Inbox;

internal static class ApplyHoldSnapshot
{
    internal static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, HoldLifecycleEvent message, CancellationToken token)
    {
        const string sql = """
            INSERT INTO pickup_desk.holds AS current
                (hold_id, copy_id, catalog_item_id, barcode, item_title, hold_state, sequence, last_message_id, occurred_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            ON CONFLICT (hold_id) DO UPDATE SET
                item_title = excluded.item_title, hold_state = excluded.hold_state, sequence = excluded.sequence,
                last_message_id = excluded.last_message_id, occurred_at = excluded.occurred_at
            WHERE current.sequence < excluded.sequence AND current.hold_state = 'held'
                AND current.copy_id = excluded.copy_id AND current.catalog_item_id = excluded.catalog_item_id
                AND current.barcode = excluded.barcode
            """;
        await using var apply = new NpgsqlCommand(sql, connection, transaction);
        apply.Parameters.AddWithValue(message.Data.HoldId);
        apply.Parameters.AddWithValue(message.Data.CopyId);
        apply.Parameters.AddWithValue(message.Data.CatalogItemId);
        apply.Parameters.AddWithValue(message.Data.Barcode);
        apply.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)message.Data.ItemTitle ?? DBNull.Value);
        apply.Parameters.AddWithValue(message.Data.State);
        apply.Parameters.AddWithValue(message.Sequence);
        apply.Parameters.AddWithValue(message.MessageId);
        apply.Parameters.AddWithValue(message.OccurredAt.ToUniversalTime());
        if (await apply.ExecuteNonQueryAsync(token) == 1)
        {
            return;
        }

        // The upsert has already locked the row. A terminal snapshot can arrive before placement.
        await using var read = new NpgsqlCommand(
            "SELECT sequence, last_message_id, copy_id, catalog_item_id, barcode FROM pickup_desk.holds WHERE hold_id = $1",
            connection, transaction);
        read.Parameters.AddWithValue(message.Data.HoldId);
        await using NpgsqlDataReader row = await read.ExecuteReaderAsync(token);
        await row.ReadAsync(token);
        bool sameCopy = row.GetGuid(2) == message.Data.CopyId &&
                        row.GetGuid(3) == message.Data.CatalogItemId &&
                        row.GetString(4) == message.Data.Barcode;
        if (sameCopy && (row.GetInt64(0) > message.Sequence || row.GetGuid(1) == message.MessageId))
        {
            return;
        }

        throw new InvalidDataException("The incoming fact conflicts with the recorded hold history.");
    }
}
