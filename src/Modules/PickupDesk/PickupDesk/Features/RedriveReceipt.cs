using System.Security.Claims;
using EquipmentLibrary.PickupDesk.Inbox;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class RedriveReceipt
{
    internal sealed record Request(string? Reason);

    internal static async Task<IResult> HandleAsync(Guid messageId, Request request, ClaimsPrincipal user, TimeProvider clock,
        [FromKeyedServices("PickupDesk")] NpgsqlDataSource database, CancellationToken token)
    {
        string? reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            return PickupProblems.Create(400, "invalid-request", "Provide a redrive reason of 1 to 500 characters.");
        }

        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid operatorId))
        {
            return Results.Forbid();
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var audit = new NpgsqlCommand("""
            INSERT INTO pickup_desk.redrives (subscriber_id, message_id, operator_id, reason, previous_failure, redriven_at)
            SELECT subscriber_id, message_id, $3, $4, failure, $5 FROM pickup_desk.inbox
            WHERE subscriber_id = $1 AND message_id = $2 AND quarantined_at IS NOT NULL FOR UPDATE
            """, connection, transaction);
        DateTimeOffset now = clock.GetUtcNow();
        audit.Parameters.AddWithValue(PickupInbox.Subscriber);
        audit.Parameters.AddWithValue(messageId);
        audit.Parameters.AddWithValue(operatorId);
        audit.Parameters.AddWithValue(reason);
        audit.Parameters.AddWithValue(now);
        if (await audit.ExecuteNonQueryAsync(token) == 0)
        {
            return PickupProblems.Create(409, "receipt-not-quarantined", "Only a quarantined receipt can be redriven.");
        }

        await using var retry = new NpgsqlCommand("""
            UPDATE pickup_desk.inbox SET available_at = $3, quarantined_at = NULL, attempts = 0, failure = NULL
            WHERE subscriber_id = $1 AND message_id = $2
            """, connection, transaction);
        retry.Parameters.AddWithValue(PickupInbox.Subscriber);
        retry.Parameters.AddWithValue(messageId);
        retry.Parameters.AddWithValue(now);
        await retry.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return Results.NoContent();
    }
}
