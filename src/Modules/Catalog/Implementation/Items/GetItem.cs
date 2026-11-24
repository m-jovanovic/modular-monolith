using EquipmentLibrary.Catalog.Contracts;

namespace EquipmentLibrary.Catalog.Items;

internal static class GetItem
{
    internal static void MapEndpoint(RouteGroupBuilder items) =>
        items.MapGet("/{itemId:guid}", Execute)
            .WithName("GetItem")
            .AllowAnonymous()
            .Produces<CatalogItemDescription>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Execute(Guid itemId, ICatalogLookup catalog, CancellationToken cancellationToken)
    {
        CatalogItemDescription? item = await catalog.GetItemAsync(itemId, cancellationToken);
        return item is null ? CatalogProblem.MissingItem() : Results.Ok(item);
    }
}
