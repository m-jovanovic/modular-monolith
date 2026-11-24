using System.Diagnostics;
using System.Text;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.PickupDesk.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace EquipmentLibrary.PickupDesk.Inbox;

internal sealed class PickupInbox([FromKeyedServices("PickupDesk")] NpgsqlDataSource database) : IPickupInbox
{
    internal const string Subscriber = "pickup-desk.queue.v1";

    public async Task AcceptAsync(HoldLifecycleEvent message, CancellationToken cancellationToken)
    {
        string payload = Encoding.UTF8.GetString(HoldEventWire.Write(message));
        await using NpgsqlCommand insert = database.CreateCommand("""
            INSERT INTO pickup_desk.inbox (subscriber_id, message_id, payload, trace_parent)
            VALUES ($1, $2, $3, $4)
            ON CONFLICT (subscriber_id, message_id) DO NOTHING
            """);
        insert.Parameters.AddWithValue(Subscriber);
        insert.Parameters.AddWithValue(message.MessageId);
        insert.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload);
        string? traceParent = Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.Id : null;
        insert.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)traceParent ?? DBNull.Value);

        if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
        {
            return;
        }

        // An acknowledgement always refers to the originally accepted fact.
        await using NpgsqlCommand compare = database.CreateCommand("""
            SELECT payload = $3 FROM pickup_desk.inbox WHERE subscriber_id = $1 AND message_id = $2
            """);
        compare.Parameters.AddWithValue(Subscriber);
        compare.Parameters.AddWithValue(message.MessageId);
        compare.Parameters.AddWithValue(NpgsqlDbType.Jsonb, payload);
        if (await compare.ExecuteScalarAsync(cancellationToken) is not true)
        {
            throw new InvalidDataException("The pickup receipt ID already identifies a different hold event.");
        }
    }
}
