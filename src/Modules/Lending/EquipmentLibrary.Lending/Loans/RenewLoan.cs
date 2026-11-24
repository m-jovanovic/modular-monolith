using System.Diagnostics;
using System.Security.Claims;
using EquipmentLibrary.Lending.Application.Loans.RenewLoan;
using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.Lending.Loans;

internal static class RenewLoan
{
    internal static async Task<IResult> HandleAsync(
        Guid loanId, ClaimsPrincipal user, RenewLoanCommandHandler handler, CancellationToken cancellationToken)
    {
        if (BorrowerIdentity.Read(user) is not Guid borrowerId)
        {
            return LendingErrors.Forbidden();
        }

        RenewLoanResult result = await handler.HandleAsync(new RenewLoanCommand(loanId, borrowerId), cancellationToken);
        return result switch
        {
            RenewLoanResult.Renewed(var loan) => Results.Ok(new LoanView(
                loan.Id, loan.CopyId, loan.CheckedOutAt, loan.DueAt, loan.Renewals, loan.ReturnedAt)),
            RenewLoanResult.Forbidden => LendingErrors.Forbidden(),
            RenewLoanResult.Missing => LendingErrors.Problem(404, "loan-not-found", "This loan does not exist."),
            RenewLoanResult.NotRenewable => LendingErrors.Problem(409, "loan-not-renewable", "Only an open loan before its due date can be renewed once."),
            _ => throw new UnreachableException()
        };
    }
}
