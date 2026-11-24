using Microsoft.AspNetCore.Http;

namespace EquipmentLibrary.Lending.Holds.Expiry;

internal static class ExpireDueHoldsEndpoint
{
    internal static async Task<IResult> HandleAsync(int? limit, ExpireDueHoldsHandler handler, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > ExpireDueHolds.MaximumLimit)
        {
            return LendingErrors.Invalid($"Use a batch limit between 1 and {ExpireDueHolds.MaximumLimit}.");
        }

        int expiredCount = await handler.HandleAsync(new ExpireDueHolds(limit ?? ExpireDueHolds.DefaultLimit), cancellationToken);
        return Results.Ok(new { expiredCount });
    }
}
