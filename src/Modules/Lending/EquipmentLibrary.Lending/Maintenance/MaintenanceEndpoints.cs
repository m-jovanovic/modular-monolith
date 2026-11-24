using System.Diagnostics;
using EquipmentLibrary.Lending.Application.Maintenance;
using EquipmentLibrary.Lending.Domain;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Npgsql;

namespace EquipmentLibrary.Lending.Maintenance;

internal static class MaintenanceEndpoints
{
    internal sealed record OpenRequest(string? Reason);
    internal sealed record CloseRequest(string? Note);

    internal static void MapMaintenance(this RouteGroupBuilder lending)
    {
        lending.MapPost("/copies/{copyId:guid}/maintenance", OpenAsync).RequireAuthorization("Lending.Enroll")
            .WithName("OpenMaintenanceOrder").Produces<MaintenanceOrder>(201);
        RouteGroupBuilder orders = lending.MapGroup("/maintenance").RequireAuthorization("Lending.Enroll");
        orders.MapGet("", ListOpenAsync).WithName("ListOpenMaintenanceOrders");
        orders.MapGet("/{orderId:guid}", GetAsync).WithName("GetMaintenanceOrder").Produces<MaintenanceOrder>();
        orders.MapPost("/{orderId:guid}/repair", (Guid orderId, CloseRequest request, MaintenanceOrderHandler handler,
            CancellationToken cancellationToken) => ResolveAsync(orderId, request, MaintenanceResolution.Repair, handler, cancellationToken))
            .WithName("RepairCopy");
        orders.MapPost("/{orderId:guid}/withdraw", (Guid orderId, CloseRequest request, MaintenanceOrderHandler handler,
            CancellationToken cancellationToken) => ResolveAsync(orderId, request, MaintenanceResolution.Withdraw, handler, cancellationToken))
            .WithName("WithdrawDamagedCopy");
    }

    private static async Task<IResult> OpenAsync(Guid copyId, OpenRequest request, MaintenanceOrderHandler handler,
        CancellationToken cancellationToken)
    {
        MaintenanceResult result = await handler.HandleAsync(new OpenMaintenanceOrder(copyId, request.Reason), cancellationToken);
        return result is MaintenanceResult.Saved saved
            ? Results.Created($"/api/lending/maintenance/{saved.Order.Id}", saved.Order)
            : Problem(result);
    }

    private static async Task<IResult> ResolveAsync(Guid orderId, CloseRequest request, MaintenanceResolution decision,
        MaintenanceOrderHandler handler, CancellationToken cancellationToken)
    {
        MaintenanceResult result = await handler.HandleAsync(new ResolveMaintenanceOrder(orderId, decision, request.Note), cancellationToken);
        return result is MaintenanceResult.Saved saved ? Results.Ok(saved.Order) : Problem(result);
    }

    private static async Task<IResult> GetAsync(Guid orderId, LendingContext context, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = context.DataSource.CreateCommand("""
            SELECT id, copy_id, reason, opened_at, outcome, resolution, closed_at FROM lending.maintenance_orders WHERE id = $1
            """).Bind(orderId);
        MaintenanceOrder? order = await command.ReadOneAsync(PostgresMaintenanceOrders.Read, cancellationToken);
        return order is not null ? Results.Ok(order) : Problem(new MaintenanceResult.OrderMissing());
    }

    private static async Task<IResult> ListOpenAsync(int? limit, int? offset, LendingContext context, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100 || offset is < 0)
        {
            return LendingErrors.Invalid("Use a nonnegative offset and a limit between 1 and 100.");
        }

        await using NpgsqlCommand command = context.DataSource.CreateCommand("""
            SELECT id, copy_id, reason, opened_at, outcome, resolution, closed_at FROM lending.maintenance_orders
            WHERE closed_at IS NULL ORDER BY opened_at, id LIMIT $1 OFFSET $2
            """).Bind(limit ?? 25, offset ?? 0);
        List<MaintenanceOrder> items = await command.ReadListAsync(PostgresMaintenanceOrders.Read, cancellationToken);
        return Results.Ok(new { items });
    }

    private static IResult Problem(MaintenanceResult result) => result switch
    {
        MaintenanceResult.InvalidNote => LendingErrors.Invalid("Provide a note between 1 and 500 characters."),
        MaintenanceResult.CopyMissing => LendingErrors.CopyNotFound(),
        MaintenanceResult.CopyUnavailable => LendingErrors.Problem(409, "copy-unavailable", "Only an available copy can enter maintenance."),
        MaintenanceResult.OrderMissing => LendingErrors.Problem(404, "maintenance-not-found", "This maintenance order does not exist."),
        MaintenanceResult.AlreadyResolved => LendingErrors.Problem(409, "maintenance-closed", "This order already has a different resolution."),
        _ => throw new UnreachableException()
    };
}
