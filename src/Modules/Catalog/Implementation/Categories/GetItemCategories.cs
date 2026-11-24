namespace EquipmentLibrary.Catalog.Categories;

internal static class GetItemCategories
{
    internal static void MapEndpoint(RouteGroupBuilder items) =>
        items.MapGet("/{itemId:guid}/categories", Execute)
            .WithName("GetItemCategories")
            .AllowAnonymous()
            .Produces<IReadOnlyList<Category>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

    private static async Task<IResult> Execute(Guid itemId, Handler handler, CancellationToken cancellationToken)
    {
        IReadOnlyList<Category>? categories = await handler.Handle(itemId, cancellationToken);
        return categories is null ? CatalogProblem.MissingItem() : Results.Ok(categories);
    }

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        public async Task<IReadOnlyList<Category>?> Handle(Guid itemId, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand query = database.CreateCommand("""
                SELECT c.id, c.name
                FROM catalog.items i
                LEFT JOIN catalog.item_categories ic ON ic.item_id = i.id
                LEFT JOIN catalog.categories c ON c.id = ic.category_id
                WHERE i.id = $1
                ORDER BY lower(c.name), c.id
                """);
            query.Parameters.AddWithValue(itemId);
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            var categories = new List<Category>();
            do
            {
                if (!reader.IsDBNull(0))
                {
                    categories.Add(new Category(reader.GetGuid(0), reader.GetString(1)));
                }
            }
            while (await reader.ReadAsync(cancellationToken));

            return categories;
        }
    }
}
