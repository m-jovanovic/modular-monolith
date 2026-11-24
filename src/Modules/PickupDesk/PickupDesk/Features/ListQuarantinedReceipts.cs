using EquipmentLibrary.PickupDesk.Inbox;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class ListQuarantinedReceipts
{
    internal sealed record Receipt(Guid MessageId, int Attempts, string Failure, DateTime QuarantinedAt);

    internal static async Task<IResult> HandleAsync(int? limit, int? offset,
        [FromKeyedServices("PickupDesk")] NpgsqlDataSource database, CancellationToken token)
    {
        if (limit is < 1 or > 100 || offset is < 0)
        {
            return PickupProblems.Create(400, "invalid-request", "Limit must be 1 to 100 and offset must be nonnegative.");
        }

        await using NpgsqlCommand query = database.CreateCommand("""
            SELECT message_id, attempts, failure, quarantined_at FROM pickup_desk.inbox
            WHERE subscriber_id = $1 AND quarantined_at IS NOT NULL
            ORDER BY quarantined_at, message_id LIMIT $2 OFFSET $3
            """);
        query.Parameters.AddWithValue(PickupInbox.Subscriber);
        query.Parameters.AddWithValue(limit ?? 50);
        query.Parameters.AddWithValue(offset ?? 0);
        await using NpgsqlDataReader row = await query.ExecuteReaderAsync(token);
        var items = new List<Receipt>();
        while (await row.ReadAsync(token))
        {
            items.Add(new(row.GetGuid(0), row.GetInt32(1), row.GetString(2), row.GetDateTime(3)));
        }

        return Results.Ok(new { items });
    }
}
