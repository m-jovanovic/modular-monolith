using System.Security.Claims;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Loans;

internal static class RenewLoan
{
    internal static async Task<IResult> HandleAsync(
        Guid loanId, ClaimsPrincipal user, LendingContext lending, CancellationToken cancellationToken)
    {
        if (BorrowerIdentity.Read(user) is not Guid borrowerId)
        {
            return LendingErrors.Forbidden();
        }

        await using NpgsqlCommand renew = lending.DataSource.CreateCommand("""
            UPDATE lending.loans SET due_at = due_at + $4, renewals = renewals + 1
            WHERE id = $1 AND borrower_id = $2 AND due_at > $3 AND returned_at IS NULL AND renewals = 0
            RETURNING id, copy_id, checked_out_at, due_at, renewals, returned_at
            """).Bind(loanId, borrowerId, lending.Clock.GetUtcNow(), LoanPolicy.LoanPeriod);
        LoanView? renewed = await renew.ReadOneAsync(LoanView.Read, cancellationToken);
        if (renewed is not null)
        {
            return Results.Ok(renewed);
        }

        await using NpgsqlCommand owner = lending.DataSource.CreateCommand("SELECT borrower_id FROM lending.loans WHERE id = $1").Bind(loanId);
        if (await owner.ExecuteScalarAsync(cancellationToken) is not Guid ownerId)
        {
            return LendingErrors.Problem(404, "loan-not-found", "This loan does not exist.");
        }

        return ownerId == borrowerId
            ? LendingErrors.Problem(409, "loan-not-renewable", "Only an open loan before its due date can be renewed once.")
            : LendingErrors.Forbidden();
    }
}
