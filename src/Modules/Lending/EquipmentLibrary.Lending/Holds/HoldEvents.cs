using System.Diagnostics;
using System.Text;
using EquipmentLibrary.Lending.Delivery;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.Lending.Persistence;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal static class HoldEvents
{
    internal static async Task RecordAsync(
        NpgsqlConnection connection, HoldSubscriptions subscriptions, HoldSnapshot snapshot, string eventType, long sequence, DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var message = new HoldLifecycleEvent(Guid.CreateVersion7(), eventType, 1, sequence, occurredAt, snapshot);
        string json = Encoding.UTF8.GetString(HoldEventWire.Write(message));
        string? traceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
        await using NpgsqlCommand insert = connection.Sql("""
            INSERT INTO lending.outbox (id, payload, occurred_at, trace_parent, subscribers, delivered_at)
            VALUES ($1, $2::jsonb, $3, NULLIF($4, ''), $5, CASE WHEN cardinality($5::text[]) = 0 THEN $3 END)
            """, message.MessageId, json, occurredAt, traceParent ?? string.Empty, subscriptions.SnapshotNames());
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
