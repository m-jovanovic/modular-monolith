namespace EquipmentLibrary.Catalog.Categories;

internal static class BrowseCategories
{
    internal static void MapEndpoints(RouteGroupBuilder categories)
    {
        categories.MapGet("/", List)
            .WithName("ListCategories")
            .AllowAnonymous()
            .Produces<IReadOnlyList<Category>>();
        categories.MapGet("/{categoryId:guid}", Get)
            .WithName("GetCategory")
            .AllowAnonymous()
            .Produces<Category>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> List(Handler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.List(cancellationToken));

    private static async Task<IResult> Get(Guid categoryId, Handler handler, CancellationToken cancellationToken)
    {
        Category? category = await handler.Get(categoryId, cancellationToken);
        return category is null ? CatalogProblem.MissingCategory() : Results.Ok(category);
    }

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        public async Task<IReadOnlyList<Category>> List(CancellationToken cancellationToken)
        {
            await using NpgsqlCommand query = database.CreateCommand(
                "SELECT id, name FROM catalog.categories ORDER BY lower(name), id");
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            var categories = new List<Category>();
            while (await reader.ReadAsync(cancellationToken))
            {
                categories.Add(new Category(reader.GetGuid(0), reader.GetString(1)));
            }

            return categories;
        }

        public async Task<Category?> Get(Guid categoryId, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand query = database.CreateCommand("SELECT id, name FROM catalog.categories WHERE id = $1");
            query.Parameters.AddWithValue(categoryId);
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? new Category(reader.GetGuid(0), reader.GetString(1)) : null;
        }
    }
}
