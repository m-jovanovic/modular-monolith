using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace EquipmentLibrary.Lending.Copies;

internal static class CopyEndpoints
{
    internal static void MapCopies(this RouteGroupBuilder lending)
    {
        lending.MapPost("/copies", EnrollCopy.HandleAsync).WithName("EnrollCopy").RequireAuthorization("Lending.Enroll")
            .Produces<CopyView>(StatusCodes.Status201Created).Produces<CopyView>()
            .ProducesProblem(400).ProducesProblem(404).ProducesProblem(409);
        lending.MapGet("/copies/{copyId:guid}", GetCopy.HandleAsync).WithName("GetCopy")
            .Produces<CopyView>().ProducesProblem(404);
        lending.MapGet("/copies", FindCopies.HandleAsync).WithName("FindCopies")
            .Produces<LendingPage<CopyView>>().ProducesProblem(400);
        lending.MapGet("/availability/{catalogItemId:guid}", GetAvailability.HandleAsync).WithName("GetAvailability")
            .Produces<GetAvailability.Availability>();
        lending.MapPost("/copies/{copyId:guid}/retire", ChangeCirculation.RetireAsync).WithName("RetireCopy").RequireAuthorization("Lending.Enroll")
            .Produces<CopyView>().ProducesProblem(404).ProducesProblem(409);
        lending.MapPost("/copies/{copyId:guid}/reinstate", ChangeCirculation.ReinstateAsync).WithName("ReinstateCopy").RequireAuthorization("Lending.Enroll")
            .Produces<CopyView>().ProducesProblem(404).ProducesProblem(409);
        lending.MapGet("/copies/{copyId:guid}/history", GetCopyHistory.HandleAsync).WithName("GetCopyHistory").RequireAuthorization("Pickup.Manage")
            .Produces<LendingPage<GetCopyHistory.Entry>>().ProducesProblem(400).ProducesProblem(404);
    }
}
