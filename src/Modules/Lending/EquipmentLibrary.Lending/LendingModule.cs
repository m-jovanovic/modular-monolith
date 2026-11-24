using EquipmentLibrary.Lending.Copies;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace EquipmentLibrary.Lending;

public static class LendingModule
{
    public static IServiceCollection AddLending(this IServiceCollection services)
    {
        services.AddScoped<LendingContext>();
        return services;
    }

    public static IEndpointRouteBuilder MapLending(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder lending = endpoints.MapGroup("/api/lending").WithTags("Lending");
        lending.MapPost("/copies", EnrollCopy.HandleAsync).RequireAuthorization("Lending.Enroll");
        lending.MapGet("/copies/{copyId:guid}", GetCopy.HandleAsync);
        lending.MapGet("/copies", FindCopies.HandleAsync);
        lending.MapGet("/availability/{catalogItemId:guid}", GetAvailability.HandleAsync);
        lending.MapPost("/holds", Holds.PlaceHold.HandleAsync).RequireAuthorization("Borrower");
        lending.MapGet("/holds/{holdId:guid}", Holds.GetHold.HandleAsync).RequireAuthorization();
        lending.MapGet("/me/holds", Holds.ListMyHolds.HandleAsync).RequireAuthorization("Borrower");
        lending.MapPost("/holds/{holdId:guid}/cancel", Holds.CloseHold.CancelAsync).RequireAuthorization("Borrower");
        lending.MapPost("/holds/{holdId:guid}/collect", Holds.CloseHold.CollectAsync).RequireAuthorization("Pickup.Manage");
        lending.MapPost("/loans/{loanId:guid}/return", Loans.ReturnLoan.HandleAsync).RequireAuthorization("Pickup.Manage");
        lending.MapPost("/loans/{loanId:guid}/renew", Loans.RenewLoan.HandleAsync).RequireAuthorization("Borrower");
        lending.MapPost("/copies/{copyId:guid}/retire", ChangeCirculation.RetireAsync).RequireAuthorization("Lending.Enroll");
        lending.MapPost("/copies/{copyId:guid}/reinstate", ChangeCirculation.ReinstateAsync).RequireAuthorization("Lending.Enroll");
        lending.MapGet("/copies/{copyId:guid}/history", GetCopyHistory.HandleAsync).RequireAuthorization("Pickup.Manage");
        lending.MapGet("/loans/{loanId:guid}", Loans.GetLoan.HandleAsync).RequireAuthorization();
        lending.MapGet("/me/loans", Loans.ListMyLoans.HandleAsync).RequireAuthorization("Borrower");
        lending.MapGet("/loans/overdue", Loans.ListOverdueLoans.HandleAsync).RequireAuthorization("Pickup.Manage");
        return endpoints;
    }
}
