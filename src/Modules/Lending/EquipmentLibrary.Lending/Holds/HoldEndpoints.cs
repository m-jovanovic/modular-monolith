using EquipmentLibrary.Lending.Holds.Expiry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EquipmentLibrary.Lending.Holds;

internal static class HoldEndpoints
{
    internal static void MapHolds(this RouteGroupBuilder lending)
    {
        lending.MapPost("/holds", PlaceHold.HandleAsync).WithName("PlaceHold").RequireAuthorization("Borrower")
            .Produces<HoldView>(StatusCodes.Status201Created).ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        lending.MapGet("/holds/{holdId:guid}", GetHold.HandleAsync).WithName("GetHold").RequireAuthorization()
            .Produces<HoldView>().ProducesProblem(404);
        lending.MapGet("/me/holds", ListMyHolds.HandleAsync).WithName("ListMyHolds").RequireAuthorization("Borrower")
            .Produces<LendingPage<HoldView>>().ProducesProblem(400);
        lending.MapPost("/holds/{holdId:guid}/cancel", CloseHold.CancelAsync).WithName("CancelHold").RequireAuthorization("Borrower")
            .Produces<HoldView>().ProducesProblem(404).ProducesProblem(409);
        lending.MapPost("/holds/{holdId:guid}/collect", CloseHold.CollectAsync).WithName("CollectHold").RequireAuthorization("Pickup.Manage")
            .Produces<HoldView>().ProducesProblem(404).ProducesProblem(409);
        lending.MapPost("/holds/expire-due", ExpireDueHoldsEndpoint.HandleAsync).WithName("ExpireDueHolds").RequireAuthorization("Pickup.Manage")
            .Produces<ExpireDueHoldsEndpoint.ExpiryResult>().ProducesProblem(400);
    }
}
