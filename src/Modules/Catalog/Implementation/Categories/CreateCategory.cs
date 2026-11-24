namespace EquipmentLibrary.Catalog.Categories;

internal static class CreateCategory
{
    internal sealed record Request(string? Name);

    internal static void MapEndpoint(RouteGroupBuilder categories) =>
        categories.MapPost("/", Execute).RequireAuthorization(CatalogModule.ManagePolicy);

    private static async Task<IResult> Execute(Request request, Handler handler, CancellationToken cancellationToken)
    {
        string? name = request.Name?.Trim();
        if (name is not { Length: > 0 and <= 80 } || name.Contains('\0'))
        {
            return CatalogProblem.Invalid("Supply a category name of 1–80 characters.");
        }

        Category? category = await handler.Handle(name, cancellationToken);
        return category is null
            ? CatalogProblem.DuplicateCategory()
            : Results.Created($"/api/catalog/categories/{category.Id}", category);
    }

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        public async Task<Category?> Handle(string name, CancellationToken cancellationToken)
        {
            var category = new Category(Guid.CreateVersion7(), name);
            await using NpgsqlCommand insert = database.CreateCommand(
                "INSERT INTO catalog.categories (id, name) VALUES ($1, $2) ON CONFLICT (lower(name)) DO NOTHING");
            insert.Parameters.AddWithValue(category.Id);
            insert.Parameters.AddWithValue(category.Name);
            return await insert.ExecuteNonQueryAsync(cancellationToken) == 1 ? category : null;
        }
    }
}
