using System.Text;
using System.Text.Json;
using EquipmentLibrary.Lending.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.PickupDesk.Inbox;

internal sealed class ProcessPickupInbox(
    [FromKeyedServices("PickupDesk")] NpgsqlDataSource database,
    TimeProvider clock)
{
    public async Task<bool> ProcessNextAsync(CancellationToken token)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var claim = new NpgsqlCommand("""
            SELECT message_id, payload::text, attempts FROM pickup_desk.inbox
            WHERE subscriber_id = $1 AND completed_at IS NULL AND quarantined_at IS NULL AND available_at <= $2
            ORDER BY available_at, received_at, message_id LIMIT 1 FOR UPDATE SKIP LOCKED
            """, connection, transaction);
        claim.Parameters.AddWithValue(PickupInbox.Subscriber);
        claim.Parameters.AddWithValue(clock.GetUtcNow());

        Guid id;
        string payload;
        int attempts;
        await using (NpgsqlDataReader row = await claim.ExecuteReaderAsync(token))
        {
            if (!await row.ReadAsync(token))
            {
                return false;
            }

            id = row.GetGuid(0);
            payload = row.GetString(1);
            attempts = row.GetInt32(2) + 1;
        }

        await transaction.SaveAsync("before_apply", token);
        try
        {
            HoldLifecycleEvent message = HoldEventWire.Read(Encoding.UTF8.GetBytes(payload));
            if (id != message.MessageId)
            {
                throw new InvalidDataException("The stored receipt ID differs from the event ID.");
            }

            await ApplyHoldSnapshot.ExecuteAsync(connection, transaction, message, token);
            await using var finish = new NpgsqlCommand("""
                UPDATE pickup_desk.inbox SET completed_at = $3, failure = NULL, attempts = attempts + 1
                WHERE subscriber_id = $1 AND message_id = $2
                """, connection, transaction);
            finish.Parameters.AddWithValue(PickupInbox.Subscriber);
            finish.Parameters.AddWithValue(id);
            finish.Parameters.AddWithValue(clock.GetUtcNow());
            await finish.ExecuteNonQueryAsync(token);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or PostgresException)
        {
            await transaction.RollbackAsync("before_apply", token);
            bool quarantine = attempts >= 5 || ex is JsonException or InvalidDataException;
            await using var fail = new NpgsqlCommand("""
                UPDATE pickup_desk.inbox SET attempts = $3, failure = $4, available_at = $5,
                    quarantined_at = CASE WHEN $6 THEN $7 ELSE NULL END
                WHERE subscriber_id = $1 AND message_id = $2
                """, connection, transaction);
            DateTimeOffset now = clock.GetUtcNow();
            fail.Parameters.AddWithValue(PickupInbox.Subscriber);
            fail.Parameters.AddWithValue(id);
            fail.Parameters.AddWithValue(attempts);
            fail.Parameters.AddWithValue(ex is PostgresException pg ? pg.SqlState : ex.GetType().Name);
            fail.Parameters.AddWithValue(now.AddSeconds(Math.Pow(2, Math.Min(attempts, 5))));
            fail.Parameters.AddWithValue(quarantine);
            fail.Parameters.AddWithValue(now);
            await fail.ExecuteNonQueryAsync(token);
        }

        await transaction.CommitAsync(token);
        return true;
    }
}
