using EquipmentLibrary.Catalog.Contracts;

namespace EquipmentLibrary.Catalog.Items;

internal static class CreateItem
{
    internal sealed record Request(string? Title, string? Description);

    internal static void MapEndpoint(RouteGroupBuilder items) =>
        items.MapPost("/", Execute)
            .WithName("CreateItem")
            .RequireAuthorization(CatalogModule.ManagePolicy)
            .Produces<CatalogItemDescription>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

    private static async Task<IResult> Execute(Request request, Handler handler, CancellationToken cancellationToken)
    {
        if (!ItemDetails.TryCreate(request.Title, request.Description, out ItemDetails? details))
        {
            return CatalogProblem.Invalid(ItemDetails.ValidationMessage);
        }

        CatalogItemDescription item = await handler.Handle(details, cancellationToken);
        return Results.Created($"/api/catalog/items/{item.Id}", item);
    }

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        public async Task<CatalogItemDescription> Handle(ItemDetails details, CancellationToken cancellationToken)
        {
            var item = new CatalogItemDescription(Guid.CreateVersion7(), details.Title, details.Description);
            await using NpgsqlCommand insert = database.CreateCommand(
                "INSERT INTO catalog.items (id, title, description) VALUES ($1, $2, $3)");
            insert.Parameters.AddWithValue(item.Id);
            insert.Parameters.AddWithValue(item.Title);
            insert.Parameters.AddWithValue(item.Description);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            return item;
        }
    }
}
