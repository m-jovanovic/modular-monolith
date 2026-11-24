using EquipmentLibrary.PickupDesk.Contracts;
using EquipmentLibrary.PickupDesk.Features;
using EquipmentLibrary.PickupDesk.Inbox;
using EquipmentLibrary.PickupDesk.Preparation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentLibrary.PickupDesk;

public static class PickupDeskModule
{
    public static IServiceCollection AddPickupDesk(this IServiceCollection services, bool runWorker = true)
    {
        services.AddSingleton<IPickupInbox, PickupInbox>();
        services.AddSingleton<ProcessPickupInbox>();
        services.AddSingleton<ChangePreparation>();
        if (runWorker)
        {
            services.AddHostedService<PickupWorker>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapPickupDesk(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/pickup-desk").WithTags("PickupDesk").RequireAuthorization("Pickup.Manage");
        group.MapGet("/queue", ListPickups.HandleAsync).WithName("ListPickups");
        group.MapGet("/queue/{holdId:guid}", GetPickup.HandleAsync).WithName("GetPickup");
        group.MapPost("/queue/{holdId:guid}/claim", ClaimPickup.HandleAsync).WithName("ClaimPickup");
        group.MapPost("/queue/{holdId:guid}/release", ReleasePickup.HandleAsync).WithName("ReleasePickup");
        group.MapPost("/queue/{holdId:guid}/ready", MarkPickupReady.HandleAsync).WithName("MarkPickupReady");
        group.MapPost("/queue/{holdId:guid}/issues", ReportPickupIssue.HandleAsync).WithName("ReportPickupIssue");
        group.MapPost("/queue/{holdId:guid}/issues/resolve", ResolvePickupIssue.HandleAsync).WithName("ResolvePickupIssue");
        group.MapGet("/workload", GetDeskWorkload.HandleAsync).WithName("GetDeskWorkload");
        group.MapGet("/history", ListClosedPickups.HandleAsync).WithName("ListClosedPickups");
        RouteGroupBuilder operations = group.MapGroup("/inbox").RequireAuthorization("Pickup.Operate");
        operations.MapGet("/quarantine", ListQuarantinedReceipts.HandleAsync).WithName("ListQuarantinedReceipts");
        operations.MapPost("/{messageId:guid}/redrive", RedriveReceipt.HandleAsync).WithName("RedriveReceipt");
        return endpoints;
    }
}
