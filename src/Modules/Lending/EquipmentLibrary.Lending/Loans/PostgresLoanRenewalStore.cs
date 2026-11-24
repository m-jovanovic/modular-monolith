using EquipmentLibrary.Lending.Application.Loans.RenewLoan;
using EquipmentLibrary.Lending.Domain;
using EquipmentLibrary.Lending.Persistence;
using Npgsql;

namespace EquipmentLibrary.Lending.Loans;

internal sealed class PostgresLoanRenewalStore(LendingContext lending) : ILoanRenewalStore
{
    public async Task<RenewLoanResult> UpdateUnderLockAsync(
        Guid loanId, Func<Loan, RenewLoanResult> decide, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await lending.DataSource.OpenConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using NpgsqlCommand read = connection.Sql("""
            SELECT id, copy_id, borrower_id, checked_out_at, due_at, renewals, returned_at
            FROM lending.loans WHERE id = $1 FOR UPDATE
            """, loanId);
        Loan? current = await read.ReadOneAsync(row => new Loan(row.GetGuid(0), row.GetGuid(1), row.GetGuid(2),
            row.GetFieldValue<DateTimeOffset>(3), row.GetFieldValue<DateTimeOffset>(4), row.GetInt16(5),
            row.IsDBNull(6) ? null : row.GetFieldValue<DateTimeOffset>(6)), cancellationToken);
        if (current is null)
        {
            return new RenewLoanResult.Missing();
        }

        RenewLoanResult decision = decide(current);
        if (decision is RenewLoanResult.Renewed renewed)
        {
            await using NpgsqlCommand write = connection.Sql("""
                UPDATE lending.loans SET due_at = $2, renewals = $3 WHERE id = $1
                """, loanId, renewed.Loan.DueAt, renewed.Loan.Renewals);
            await write.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return decision;
    }
}
