using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class GetDeskWorkload
{
    internal static async Task<IResult> HandleAsync([FromKeyedServices("PickupDesk")] NpgsqlDataSource database, CancellationToken token)
    {
        await using NpgsqlCommand query = database.CreateCommand("""
            SELECT count(*) FILTER (WHERE assigned_to IS NULL),
                count(*) FILTER (WHERE assigned_to IS NOT NULL AND ready_at IS NULL AND issue IS NULL),
                count(*) FILTER (WHERE issue IS NOT NULL), count(*) FILTER (WHERE ready_at IS NOT NULL),
                min(occurred_at)
            FROM pickup_desk.holds WHERE hold_state = 'held'
            """);
        await using NpgsqlDataReader row = await query.ExecuteReaderAsync(token);
        await row.ReadAsync(token);
        return Results.Ok(new
        {
            unclaimed = row.GetInt64(0), inPreparation = row.GetInt64(1),
            blocked = row.GetInt64(2), ready = row.GetInt64(3),
            oldestHoldAt = row.IsDBNull(4) ? (DateTime?)null : row.GetDateTime(4)
        });
    }
}
