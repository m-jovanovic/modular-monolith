using System.Security.Claims;
using EquipmentLibrary.PickupDesk.Preparation;
using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class ClaimPickup
{
    internal static Task<IResult> HandleAsync(Guid holdId, ClaimsPrincipal staff, ChangePreparation preparation, CancellationToken token) =>
        preparation.ExecuteAsync(holdId, staff, (pickup, staffId) => pickup.Claim(staffId), token);
}
