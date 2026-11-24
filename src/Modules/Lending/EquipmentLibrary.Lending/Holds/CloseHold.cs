using System.Diagnostics;
using System.Security.Claims;
using EquipmentLibrary.Lending.Application.Holds.CloseHold;
using EquipmentLibrary.Lending.Domain;
using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.Lending.Holds;

internal static class CloseHold
{
    internal static async Task<IResult> CancelAsync(Guid holdId, ClaimsPrincipal user, CloseHoldCommandHandler handler, CancellationToken cancellationToken)
    {
        if (BorrowerIdentity.Read(user) is not Guid borrowerId)
        {
            return LendingErrors.Forbidden();
        }

        return ToResponse(await handler.HandleAsync(new CancelHoldCommand(holdId, borrowerId), cancellationToken));
    }

    internal static async Task<IResult> CollectAsync(Guid holdId, CloseHoldCommandHandler handler, CancellationToken cancellationToken) =>
        ToResponse(await handler.HandleAsync(new CollectHoldCommand(holdId), cancellationToken));

    private static IResult ToResponse(CloseHoldResult result) => result switch
    {
        CloseHoldResult.Closed closed => Results.Ok(new HoldView(closed.HoldId, closed.CopyId, closed.State, closed.Window.Deadline, Expired: false)),
        CloseHoldResult.MissingHold => LendingErrors.Problem(404, "hold-not-found", "This hold does not exist."),
        CloseHoldResult.Refused { Reason: HoldClosureRefusal.OtherBorrower } => LendingErrors.Forbidden(),
        CloseHoldResult.Refused { Reason: HoldClosureRefusal.AlreadyClosed } => LendingErrors.Problem(409, "hold-not-active", "This hold is already closed."),
        CloseHoldResult.Refused { Reason: HoldClosureRefusal.PickupWindowClosed } => LendingErrors.Problem(409, "pickup-window-closed", "The pickup window is closed."),
        _ => throw new UnreachableException()
    };
}
