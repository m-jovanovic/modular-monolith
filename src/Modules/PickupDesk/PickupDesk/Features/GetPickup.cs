using EquipmentLibrary.PickupDesk.Queries;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class GetPickup
{
    internal static async Task<IResult> HandleAsync(Guid holdId,
        [FromKeyedServices("PickupDesk")] NpgsqlDataSource database, CancellationToken token)
    {
        await using NpgsqlCommand query = database.CreateCommand(
            $"SELECT {PickupView.Columns} FROM pickup_desk.holds WHERE hold_id = $1 AND hold_state = 'held'");
        query.Parameters.AddWithValue(holdId);
        await using NpgsqlDataReader row = await query.ExecuteReaderAsync(token);
        return await row.ReadAsync(token) ? Results.Ok(PickupView.Read(row)) : PickupProblems.Missing();
    }
}
