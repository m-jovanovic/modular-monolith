using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EquipmentLibrary.Lending.Loans;

internal static class LoanEndpoints
{
    internal static void MapLoans(this RouteGroupBuilder lending)
    {
        lending.MapGet("/loans/{loanId:guid}", GetLoan.HandleAsync).WithName("GetLoan").RequireAuthorization()
            .Produces<LoanView>().ProducesProblem(404);
        lending.MapGet("/me/loans", ListMyLoans.HandleAsync).WithName("ListMyLoans").RequireAuthorization("Borrower")
            .Produces<LendingPage<LoanView>>().ProducesProblem(400);
        lending.MapGet("/loans/overdue", ListOverdueLoans.HandleAsync).WithName("ListOverdueLoans").RequireAuthorization("Pickup.Manage")
            .Produces<LendingPage<LoanView>>().ProducesProblem(400);
        lending.MapPost("/loans/{loanId:guid}/return", ReturnLoan.HandleAsync).WithName("ReturnLoan").RequireAuthorization("Pickup.Manage")
            .Produces<LoanView>().ProducesProblem(404);
        lending.MapPost("/loans/{loanId:guid}/renew", RenewLoan.HandleAsync).WithName("RenewLoan").RequireAuthorization("Borrower")
            .Produces<LoanView>().ProducesProblem(404).ProducesProblem(409);
    }
}
