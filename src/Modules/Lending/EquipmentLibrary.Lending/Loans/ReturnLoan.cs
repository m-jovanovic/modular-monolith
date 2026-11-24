using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Loans;

internal static class ReturnLoan
{
    internal static async Task<IResult> HandleAsync(Guid loanId, LendingContext lending, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await lending.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using NpgsqlCommand markReturned = connection.Sql("""
            UPDATE lending.loans SET returned_at = $2 WHERE id = $1 AND returned_at IS NULL
            RETURNING id, copy_id, checked_out_at, due_at, renewals, returned_at
            """, loanId, lending.Clock.GetUtcNow());
        LoanView? loan = await markReturned.ReadOneAsync(LoanView.Read, cancellationToken);
        if (loan is null)
        {
            // Retries after a later checkout must never release the newer borrower's loan.
            await using NpgsqlCommand read = connection.Sql("""
                SELECT id, copy_id, checked_out_at, due_at, renewals, returned_at FROM lending.loans WHERE id = $1
                """, loanId);
            LoanView? previous = await read.ReadOneAsync(LoanView.Read, cancellationToken);
            return previous is null
                ? LendingErrors.Problem(404, "loan-not-found", "This loan does not exist.")
                : Results.Ok(previous);
        }

        await using NpgsqlCommand release = connection.Sql("UPDATE lending.copies SET state = 'available' WHERE id = $1", loan.CopyId);
        await release.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(loan);
    }
}
