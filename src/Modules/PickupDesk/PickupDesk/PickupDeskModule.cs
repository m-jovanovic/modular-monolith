using EquipmentLibrary.PickupDesk.Contracts;
using EquipmentLibrary.PickupDesk.Features;
using EquipmentLibrary.PickupDesk.Inbox;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentLibrary.PickupDesk;

public static class PickupDeskModule
{
    public static IServiceCollection AddPickupDesk(this IServiceCollection services, bool runWorker = true)
    {
        services.AddSingleton<IPickupInbox, PickupInbox>();
        services.AddSingleton<ProcessPickupInbox>();
        if (runWorker)
        {
            services.AddHostedService<PickupWorker>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapPickupDesk(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/pickup-desk").RequireAuthorization("Pickup.Manage");
        group.MapGet("/queue", ListPickups.HandleAsync);
        group.MapGet("/queue/{holdId:guid}", GetPickup.HandleAsync);
        return endpoints;
    }
}
