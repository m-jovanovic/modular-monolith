using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.Lending.Delivery;

public static class DeliveryOperations
{
    public static void MapDeliveryOperations(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder delivery = endpoints.MapGroup("/api/lending/delivery").WithTags("Lending delivery").RequireAuthorization("Pickup.Operate");
        delivery.MapGet("/status", GetStatusAsync).WithName("GetDeliveryStatus");
        delivery.MapGet("/quarantine", ListQuarantinedAsync).WithName("ListQuarantinedDeliveries");
        delivery.MapPost("/quarantine/{messageId:guid}/redrive", RedriveAsync).WithName("RedriveDelivery");
    }

    private static async Task<IResult> GetStatusAsync(
        [FromKeyedServices("Lending")] NpgsqlDataSource database, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = database.CreateCommand("""
            SELECT count(*) FILTER (WHERE delivered_at IS NULL AND quarantined_at IS NULL),
                   count(*) FILTER (WHERE quarantined_at IS NOT NULL),
                   count(*) FILTER (WHERE delivered_at IS NOT NULL),
                   coalesce(extract(epoch FROM now() - min(occurred_at) FILTER
                       (WHERE delivered_at IS NULL AND quarantined_at IS NULL)), 0)::double precision
            FROM lending.outbox
            """);
        await using NpgsqlDataReader row = await command.ExecuteReaderAsync(cancellationToken);
        await row.ReadAsync(cancellationToken);
        return Results.Ok(new { Pending = row.GetInt64(0), Quarantined = row.GetInt64(1),
            Delivered = row.GetInt64(2), OldestPendingSeconds = row.GetDouble(3) });
    }

    private static async Task<IResult> ListQuarantinedAsync(
        [FromKeyedServices("Lending")] NpgsqlDataSource database, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = database.CreateCommand("""
            SELECT id, failure_count, last_failure, quarantined_at FROM lending.outbox
            WHERE quarantined_at IS NOT NULL ORDER BY quarantined_at, id LIMIT 100
            """);
        await using NpgsqlDataReader rows = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<QuarantinedDelivery>();
        while (await rows.ReadAsync(cancellationToken))
        {
            items.Add(new(rows.GetGuid(0), rows.GetInt32(1), rows.GetString(2), rows.GetDateTime(3)));
        }

        return Results.Ok(items);
    }

    private static async Task<IResult> RedriveAsync(Guid messageId, ClaimsPrincipal user,
        [FromKeyedServices("Lending")] NpgsqlDataSource database, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out Guid operatorId))
        {
            return Results.Forbid();
        }

        await using NpgsqlCommand command = database.CreateCommand("""
            WITH changed AS (
                UPDATE lending.outbox SET failure_count = 0, last_failure = NULL,
                    quarantined_at = NULL, lease_id = NULL, eligible_at = now()
                WHERE id = $1 AND quarantined_at IS NOT NULL RETURNING id
            )
            INSERT INTO lending.outbox_redrives (message_id, operator_id) SELECT id, $2 FROM changed
            """);
        command.Parameters.AddWithValue(messageId);
        command.Parameters.AddWithValue(operatorId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1
            ? Results.NoContent()
            : Results.NotFound();
    }

    private sealed record QuarantinedDelivery(Guid MessageId, int Attempts, string Reason, DateTime QuarantinedAt);
}
