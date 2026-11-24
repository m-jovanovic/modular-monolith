using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Loans;

internal static class ListOverdueLoans
{
    internal static async Task<IResult> HandleAsync(int? limit, int? offset, LendingContext lending, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100 || offset is < 0)
        {
            return LendingErrors.Invalid("Use a nonnegative offset and limit between 1 and 100.");
        }

        await using NpgsqlCommand query = lending.DataSource.CreateCommand("""
            SELECT id, copy_id, checked_out_at, due_at, renewals, returned_at
            FROM lending.loans WHERE returned_at IS NULL AND due_at <= $1 ORDER BY due_at, id LIMIT $2 OFFSET $3
            """).Bind(lending.Clock.GetUtcNow(), limit ?? 25, offset ?? 0);
        List<LoanView> items = await query.ReadListAsync(LoanView.Read, cancellationToken);
        return Results.Ok(new LendingPage<LoanView>(items));
    }
}
