using EquipmentLibrary.Lending.Application.Maintenance;
using EquipmentLibrary.Lending.Maintenance;
using EquipmentLibrary.Lending.Application.Holds.PlaceHold;
using EquipmentLibrary.Lending.Copies;
using EquipmentLibrary.Lending.Delivery;
using EquipmentLibrary.Lending.Holds;
using EquipmentLibrary.Lending.Holds.Expiry;
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
        lending.MapPost("/loans/{loanId:guid}/return", Loans.ReturnLoan.HandleAsync).WithName("ReturnLoan").RequireAuthorization("Pickup.Manage");
        lending.MapPost("/loans/{loanId:guid}/renew", Loans.RenewLoan.HandleAsync).WithName("RenewLoan").RequireAuthorization("Borrower");
        lending.MapGet("/loans/{loanId:guid}", Loans.GetLoan.HandleAsync).WithName("GetLoan").RequireAuthorization();
        lending.MapGet("/me/loans", Loans.ListMyLoans.HandleAsync).WithName("ListMyLoans").RequireAuthorization("Borrower");
        lending.MapGet("/loans/overdue", Loans.ListOverdueLoans.HandleAsync).WithName("ListOverdueLoans").RequireAuthorization("Pickup.Manage");
        lending.MapMaintenance();
        return endpoints;
    }
}
