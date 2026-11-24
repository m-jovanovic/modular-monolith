using EquipmentLibrary.Lending.Application.Maintenance;
using EquipmentLibrary.Lending.Maintenance;
using EquipmentLibrary.Lending.Application.Holds.CloseHold;
using EquipmentLibrary.Lending.Application.Holds.PlaceHold;
using EquipmentLibrary.Lending.Copies;
using EquipmentLibrary.Lending.Delivery;
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
        lending.MapPost("/copies", EnrollCopy.HandleAsync).WithName("EnrollCopy").RequireAuthorization("Lending.Enroll");
        lending.MapGet("/copies/{copyId:guid}", GetCopy.HandleAsync).WithName("GetCopy");
        lending.MapGet("/copies", FindCopies.HandleAsync).WithName("FindCopies");
        lending.MapGet("/availability/{catalogItemId:guid}", GetAvailability.HandleAsync).WithName("GetAvailability");
        lending.MapPost("/holds", Holds.PlaceHold.HandleAsync).RequireAuthorization("Borrower")
            .WithName("PlaceHold").Produces<Holds.HoldView>(StatusCodes.Status201Created)
            .ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        lending.MapGet("/holds/{holdId:guid}", Holds.GetHold.HandleAsync).WithName("GetHold").RequireAuthorization();
        lending.MapPost("/holds/expire-due", ExpireDueHoldsEndpoint.HandleAsync).WithName("ExpireDueHolds").RequireAuthorization("Pickup.Manage");
        lending.MapGet("/me/holds", Holds.ListMyHolds.HandleAsync).WithName("ListMyHolds").RequireAuthorization("Borrower");
        lending.MapPost("/holds/{holdId:guid}/cancel", Holds.CloseHold.CancelAsync).WithName("CancelHold").RequireAuthorization("Borrower");
        lending.MapPost("/holds/{holdId:guid}/collect", Holds.CloseHold.CollectAsync).WithName("CollectHold").RequireAuthorization("Pickup.Manage");
        lending.MapPost("/loans/{loanId:guid}/return", Loans.ReturnLoan.HandleAsync).WithName("ReturnLoan").RequireAuthorization("Pickup.Manage");
        lending.MapPost("/loans/{loanId:guid}/renew", Loans.RenewLoan.HandleAsync).WithName("RenewLoan").RequireAuthorization("Borrower");
        lending.MapPost("/copies/{copyId:guid}/retire", ChangeCirculation.RetireAsync).WithName("RetireCopy").RequireAuthorization("Lending.Enroll");
        lending.MapPost("/copies/{copyId:guid}/reinstate", ChangeCirculation.ReinstateAsync).WithName("ReinstateCopy").RequireAuthorization("Lending.Enroll");
        lending.MapGet("/copies/{copyId:guid}/history", GetCopyHistory.HandleAsync).WithName("GetCopyHistory").RequireAuthorization("Pickup.Manage");
        lending.MapGet("/loans/{loanId:guid}", Loans.GetLoan.HandleAsync).WithName("GetLoan").RequireAuthorization();
        lending.MapGet("/me/loans", Loans.ListMyLoans.HandleAsync).WithName("ListMyLoans").RequireAuthorization("Borrower");
        lending.MapGet("/loans/overdue", Loans.ListOverdueLoans.HandleAsync).WithName("ListOverdueLoans").RequireAuthorization("Pickup.Manage");
        lending.MapMaintenance();
        return endpoints;
    }
}
