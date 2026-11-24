using System.Security.Claims;
using EquipmentLibrary.PickupDesk.Preparation;
using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class MarkPickupReady
{
    internal static Task<IResult> HandleAsync(Guid holdId, ClaimsPrincipal staff, ChangePreparation preparation, TimeProvider clock, CancellationToken token) =>
        preparation.ExecuteAsync(holdId, staff, (pickup, staffId) => pickup.MarkReady(staffId, clock.GetUtcNow()), token);
}
