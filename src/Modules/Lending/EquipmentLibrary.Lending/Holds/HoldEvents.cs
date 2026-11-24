using System.Text;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.Lending.Persistence;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal static class HoldEvents
{
    internal static async Task RecordAsync(
        NpgsqlConnection connection, HoldSnapshot snapshot, string eventType, long sequence, DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var message = new HoldLifecycleEvent(Guid.CreateVersion7(), eventType, 1, sequence, occurredAt, snapshot);
        string json = Encoding.UTF8.GetString(HoldEventWire.Write(message));
        await using NpgsqlCommand insert = connection.Sql("""
            INSERT INTO lending.outbox (id, payload, occurred_at) VALUES ($1, $2::jsonb, $3)
            """, message.MessageId, json, occurredAt);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
