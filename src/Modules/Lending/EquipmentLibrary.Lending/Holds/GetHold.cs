using System.Security.Claims;
using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Holds;

internal static class GetHold
{
    private sealed record OwnedHold(HoldView View, Guid OwnerId);

    internal static async Task<IResult> HandleAsync(Guid holdId, ClaimsPrincipal user, LendingContext lending, CancellationToken cancellationToken)
    {
        bool isStaff = user.HasClaim("permission", "Pickup.Manage");
        Guid? borrowerId = BorrowerIdentity.Read(user);
        if (!isStaff && !user.HasClaim("permission", "Borrower"))
        {
            return LendingErrors.Forbidden();
        }

        await using NpgsqlCommand find = lending.DataSource.CreateCommand("""
            SELECT id, copy_id, state, borrower_id FROM lending.holds WHERE id = $1
            """).Bind(holdId);
        OwnedHold? hold = await find.ReadOneAsync(
            r => new OwnedHold(HoldView.Read(r), r.GetGuid(3)), cancellationToken);
        if (hold is null)
        {
            return LendingErrors.Problem(404, "hold-not-found", "This hold does not exist.");
        }

        return isStaff || hold.OwnerId == borrowerId ? Results.Ok(hold.View) : LendingErrors.Forbidden();
    }
}
