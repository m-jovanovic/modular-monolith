using System.Security.Claims;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal static class ListMyHolds
{
    internal static async Task<IResult> HandleAsync(
        ClaimsPrincipal user, int? limit, int? offset, LendingContext lending, CancellationToken cancellationToken)
    {
        if (BorrowerIdentity.Read(user) is not Guid borrowerId)
        {
            return LendingErrors.Forbidden();
        }

        if (limit is < 1 or > 100 || offset is < 0)
        {
            return LendingErrors.Invalid("Use a nonnegative offset and limit between 1 and 100.");
        }

        await using NpgsqlCommand find = lending.DataSource.CreateCommand("""
            SELECT id, copy_id, state, placed_at, expired FROM lending.holds WHERE borrower_id = $1
            ORDER BY placed_at DESC, id LIMIT $2 OFFSET $3
            """).Bind(borrowerId, limit ?? 25, offset ?? 0);
        List<HoldView> items = await find.ReadListAsync(HoldView.Read, cancellationToken);
        return Results.Ok(new { items });
    }
}
