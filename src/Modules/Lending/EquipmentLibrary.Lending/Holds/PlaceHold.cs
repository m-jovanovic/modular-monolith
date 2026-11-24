using System.Diagnostics;
using System.Security.Claims;
using EquipmentLibrary.Lending.Application.Holds.PlaceHold;
using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.Lending.Holds;

internal static class PlaceHold
{
    internal sealed record Request(Guid CopyId);

    internal static async Task<IResult> HandleAsync(
        Request request, ClaimsPrincipal user, PlaceHoldCommandHandler handler, CancellationToken cancellationToken)
    {
        if (BorrowerIdentity.Read(user) is not Guid borrowerId)
        {
            return LendingErrors.Forbidden();
        }

        PlaceHoldResult result = await handler.HandleAsync(new PlaceHoldCommand(request.CopyId, borrowerId), cancellationToken);
        return result switch
        {
            PlaceHoldResult.Placed placed => Results.Created($"/api/lending/holds/{placed.HoldId}",
                new HoldView(placed.HoldId, placed.CopyId, "held", placed.Window.Deadline, Expired: false)),
            PlaceHoldResult.MissingCopy => LendingErrors.CopyNotFound(),
            PlaceHoldResult.Unavailable => LendingErrors.Problem(409, "copy-unavailable", "This copy is unavailable."),
            PlaceHoldResult.InvalidCopy => LendingErrors.Invalid("Provide the physical copy ID."),
            _ => throw new UnreachableException()
        };
    }
}
