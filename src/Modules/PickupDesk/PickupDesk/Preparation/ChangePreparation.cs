using System.Security.Claims;
using EquipmentLibrary.PickupDesk.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace EquipmentLibrary.PickupDesk.Preparation;

internal sealed class ChangePreparation([FromKeyedServices("PickupDesk")] NpgsqlDataSource database)
{
    internal async Task<IResult> ExecuteAsync(Guid holdId, ClaimsPrincipal staff,
        Func<StaffPreparation, Guid, string?> change, CancellationToken token)
    {
        if (!Guid.TryParse(staff.FindFirstValue(ClaimTypes.NameIdentifier), out Guid staffId))
        {
            return Results.Forbid();
        }

        await using NpgsqlConnection connection = await database.OpenConnectionAsync(token);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(token);
        await using var read = new NpgsqlCommand("""
            SELECT hold_state, assigned_to, ready_at, issue FROM pickup_desk.holds WHERE hold_id = $1 FOR UPDATE
            """, connection, transaction);
        read.Parameters.AddWithValue(holdId);

        StaffPreparation preparation;
        await using (NpgsqlDataReader row = await read.ExecuteReaderAsync(token))
        {
            if (!await row.ReadAsync(token))
            {
                return PickupProblems.Missing();
            }

            if (row.GetString(0) != "held")
            {
                return PickupProblems.Create(409, "pickup-closed", "The hold is already closed.");
            }

            preparation = new StaffPreparation(row.IsDBNull(1) ? null : row.GetGuid(1),
                row.IsDBNull(2) ? null : row.GetDateTime(2), row.IsDBNull(3) ? null : row.GetString(3));
        }

        string? conflict = change(preparation, staffId);
        if (conflict is not null)
        {
            return PickupProblems.Create(409, conflict, "The pickup cannot make that preparation transition.");
        }

        await using var write = new NpgsqlCommand("""
            UPDATE pickup_desk.holds SET assigned_to = $2, ready_at = $3, issue = $4 WHERE hold_id = $1
            """, connection, transaction);
        write.Parameters.AddWithValue(holdId);
        write.Parameters.AddWithValue(NpgsqlDbType.Uuid, (object?)preparation.AssignedTo ?? DBNull.Value);
        write.Parameters.AddWithValue(NpgsqlDbType.TimestampTz, (object?)preparation.ReadyAt ?? DBNull.Value);
        write.Parameters.AddWithValue(NpgsqlDbType.Text, (object?)preparation.Issue ?? DBNull.Value);
        await write.ExecuteNonQueryAsync(token);
        await transaction.CommitAsync(token);
        return Results.NoContent();
    }
}
