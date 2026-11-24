using EquipmentLibrary.Catalog.Contracts;

namespace EquipmentLibrary.Catalog.Items;

internal static class UpdateItem
{
    internal sealed record Request(string? Title, string? Description);

    internal static void MapEndpoint(RouteGroupBuilder items) =>
        items.MapPut("/{itemId:guid}", Execute).WithName("UpdateItem").RequireAuthorization(CatalogModule.ManagePolicy);

    private static async Task<IResult> Execute(Guid itemId, Request request, Handler handler, CancellationToken cancellationToken)
    {
        if (!ItemDetails.TryCreate(request.Title, request.Description, out ItemDetails? details))
        {
            return CatalogProblem.Invalid(ItemDetails.ValidationMessage);
        }

        CatalogItemDescription? item = await handler.Handle(itemId, details, cancellationToken);
        return item is null ? CatalogProblem.MissingItem() : Results.Ok(item);
    }

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        public async Task<CatalogItemDescription?> Handle(Guid itemId, ItemDetails details, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand update = database.CreateCommand(
                "UPDATE catalog.items SET title = $2, description = $3 WHERE id = $1");
            update.Parameters.AddWithValue(itemId);
            update.Parameters.AddWithValue(details.Title);
            update.Parameters.AddWithValue(details.Description);
            int affected = await update.ExecuteNonQueryAsync(cancellationToken);
            return affected == 0 ? null : new CatalogItemDescription(itemId, details.Title, details.Description);
        }
    }
}
