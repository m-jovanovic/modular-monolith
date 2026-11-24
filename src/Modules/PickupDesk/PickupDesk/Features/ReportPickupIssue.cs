using System.Security.Claims;
using EquipmentLibrary.PickupDesk.Preparation;
using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.PickupDesk.Features;

internal static class ReportPickupIssue
{
    internal sealed record Request(string? Reason);

    internal static Task<IResult> HandleAsync(Guid holdId, Request request, ClaimsPrincipal staff,
        ChangePreparation preparation, CancellationToken token)
    {
        string? reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            return Task.FromResult(PickupProblems.Create(400, "invalid-request", "Provide an issue of 1 to 500 characters."));
        }

        return preparation.ExecuteAsync(holdId, staff, (pickup, staffId) => pickup.ReportIssue(staffId, reason), token);
    }
}
