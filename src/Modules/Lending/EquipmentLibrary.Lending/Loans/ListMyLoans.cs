using System.Security.Claims;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Loans;

internal static class ListMyLoans
{
    internal static async Task<IResult> HandleAsync(
        ClaimsPrincipal user, bool? openOnly, int? limit, int? offset, LendingContext lending, CancellationToken cancellationToken)
    {
        if (BorrowerIdentity.Read(user) is not Guid borrowerId)
        {
            return LendingErrors.Forbidden();
        }

        if (limit is < 1 or > 100 || offset is < 0)
        {
            return LendingErrors.Invalid("Use a nonnegative offset and limit between 1 and 100.");
        }

        await using NpgsqlCommand query = lending.DataSource.CreateCommand("""
            SELECT id, copy_id, checked_out_at, due_at, renewals, returned_at
            FROM lending.loans WHERE borrower_id = $1 AND ($2 = FALSE OR returned_at IS NULL)
            ORDER BY checked_out_at DESC, id LIMIT $3 OFFSET $4
            """).Bind(borrowerId, openOnly ?? false, limit ?? 25, offset ?? 0);
        List<LoanView> items = await query.ReadListAsync(LoanView.Read, cancellationToken);
        return Results.Ok(new { items });
    }
}
