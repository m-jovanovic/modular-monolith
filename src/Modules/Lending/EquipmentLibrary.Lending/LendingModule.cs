using EquipmentLibrary.Lending.Application.Holds.CloseHold;
using EquipmentLibrary.Lending.Application.Holds.PlaceHold;
using EquipmentLibrary.Lending.Application.Loans.RenewLoan;
using EquipmentLibrary.Lending.Application.Maintenance;
using EquipmentLibrary.Lending.Copies;
using EquipmentLibrary.Lending.Delivery;
using EquipmentLibrary.Lending.Holds;
using EquipmentLibrary.Lending.Holds.Expiry;
using EquipmentLibrary.Lending.Loans;
using EquipmentLibrary.Lending.Maintenance;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EquipmentLibrary.Lending;

public static class LendingModule
{
    public static IServiceCollection AddLending(this IServiceCollection services, bool runExpiryWorker = true)
    {
        services.TryAddSingleton<HoldSubscriptions>();
        services.AddScoped<LendingContext>();
        services.AddScoped<IMaintenanceOrders, PostgresMaintenanceOrders>();
        services.AddScoped<MaintenanceOrderHandler>();
        services.AddScoped<IHoldReservationStore, Holds.PostgresHoldReservationStore>();
        services.AddScoped<PlaceHoldCommandHandler>();
        services.AddScoped<ILoanRenewalStore, PostgresLoanRenewalStore>();
        services.AddScoped<RenewLoanCommandHandler>();
        services.AddScoped<IHoldClosureStore, Holds.PostgresHoldClosureStore>();
        services.AddScoped<CloseHoldCommandHandler>();
        services.AddScoped<ExpireDueHoldsHandler>();
        if (runExpiryWorker)
        {
            services.AddHostedService<ExpireDueHoldsWorker>();
        }

        return services;
    }

    public static IEndpointRouteBuilder MapLending(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder lending = endpoints.MapGroup("/api/lending").WithTags("Lending");
        lending.MapCopies();
        lending.MapHolds();
        lending.MapLoans();
        lending.MapMaintenance();
        return endpoints;
    }
}
