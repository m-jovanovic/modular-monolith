using System.Security.Claims;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Loans;

internal static class GetLoan
{
    private sealed record OwnedLoan(LoanView View, Guid BorrowerId);

    internal static async Task<IResult> HandleAsync(Guid loanId, ClaimsPrincipal user, LendingContext lending, CancellationToken cancellationToken)
    {
        bool isStaff = user.HasClaim("permission", "Pickup.Manage");
        if (!isStaff && !user.HasClaim("permission", "Borrower"))
        {
            return LendingErrors.Forbidden();
        }

        await using NpgsqlCommand read = lending.DataSource.CreateCommand("""
            SELECT id, copy_id, checked_out_at, due_at, renewals, returned_at, borrower_id
            FROM lending.loans WHERE id = $1
            """).Bind(loanId);
        OwnedLoan? loan = await read.ReadOneAsync(r => new OwnedLoan(LoanView.Read(r), r.GetGuid(6)), cancellationToken);
        if (loan is null)
        {
            return LendingErrors.Problem(404, "loan-not-found", "This loan does not exist.");
        }

        return isStaff || loan.BorrowerId == BorrowerIdentity.Read(user) ? Results.Ok(loan.View) : LendingErrors.Forbidden();
    }
}
