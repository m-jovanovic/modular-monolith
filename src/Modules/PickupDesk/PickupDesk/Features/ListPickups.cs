using EquipmentLibrary.PickupDesk.Queries;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class ListPickups
{
    internal static async Task<IResult> HandleAsync(
        Guid? copyId, Guid? assignedTo, int? limit, int? offset,
        [FromKeyedServices("PickupDesk")] NpgsqlDataSource database, CancellationToken token)
    {
        if (limit is < 1 or > 100 || offset is < 0)
        {
            return PickupProblems.Create(400, "invalid-request", "Limit must be 1 to 100 and offset must be nonnegative.");
        }

        await using NpgsqlCommand query = database.CreateCommand($"""
            SELECT {PickupView.Columns} FROM pickup_desk.holds
            WHERE hold_state = 'held' AND ($1::uuid IS NULL OR copy_id = $1)
                AND ($2::uuid IS NULL OR assigned_to = $2)
            ORDER BY occurred_at, hold_id LIMIT $3 OFFSET $4
            """);
        query.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)copyId ?? DBNull.Value);
        query.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)assignedTo ?? DBNull.Value);
        query.Parameters.AddWithValue(limit ?? 50);
        query.Parameters.AddWithValue(offset ?? 0);
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(token);
        var items = new List<PickupView>();
        while (await reader.ReadAsync(token))
        {
            items.Add(PickupView.Read(reader));
        }

        return Results.Ok(new { items });
    }
}
