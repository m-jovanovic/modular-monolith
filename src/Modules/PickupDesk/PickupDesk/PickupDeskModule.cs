using EquipmentLibrary.PickupDesk.Contracts;
using EquipmentLibrary.PickupDesk.Features;
using EquipmentLibrary.PickupDesk.Inbox;
using EquipmentLibrary.PickupDesk.Preparation;
using EquipmentLibrary.PickupDesk.Queries;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentLibrary.PickupDesk;

public static class PickupDeskModule
{
    public static IServiceCollection AddPickupDesk(this IServiceCollection services, bool runWorker = true)
    {
        services.AddHealthChecks().AddCheck<PickupDeskReadiness>("pickup-desk-storage");
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
        group.MapGet("/queue", ListPickups.HandleAsync).WithName("ListPickups")
            .Produces<PickupList>().ProducesProblem(400);
        group.MapGet("/queue/{holdId:guid}", GetPickup.HandleAsync).WithName("GetPickup")
            .Produces<PickupView>().ProducesProblem(404);
        group.MapPost("/queue/{holdId:guid}/claim", ClaimPickup.HandleAsync).WithName("ClaimPickup")
            .Produces(204).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/queue/{holdId:guid}/release", ReleasePickup.HandleAsync).WithName("ReleasePickup")
            .Produces(204).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/queue/{holdId:guid}/ready", MarkPickupReady.HandleAsync).WithName("MarkPickupReady")
            .Produces(204).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/queue/{holdId:guid}/issues", ReportPickupIssue.HandleAsync).WithName("ReportPickupIssue")
            .Produces(204).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        group.MapPost("/queue/{holdId:guid}/issues/resolve", ResolvePickupIssue.HandleAsync).WithName("ResolvePickupIssue")
            .Produces(204).ProducesProblem(404).ProducesProblem(409);
        group.MapGet("/workload", GetDeskWorkload.HandleAsync).WithName("GetDeskWorkload")
            .Produces<GetDeskWorkload.Workload>();
        group.MapGet("/history", ListClosedPickups.HandleAsync).WithName("ListClosedPickups")
            .Produces<PickupList>().ProducesProblem(400);
        RouteGroupBuilder operations = group.MapGroup("/inbox").RequireAuthorization("Pickup.Operate");
        operations.MapGet("/quarantine", ListQuarantinedReceipts.HandleAsync).WithName("ListQuarantinedReceipts")
            .Produces<ListQuarantinedReceipts.ReceiptList>().ProducesProblem(400);
        operations.MapPost("/{messageId:guid}/redrive", RedriveReceipt.HandleAsync).WithName("RedriveReceipt")
            .Produces(204).ProducesProblem(400).ProducesProblem(409);
        return endpoints;
    }
}
