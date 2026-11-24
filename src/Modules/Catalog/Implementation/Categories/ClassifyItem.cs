namespace EquipmentLibrary.Catalog.Categories;

internal static class ClassifyItem
{
    internal static void MapEndpoints(RouteGroupBuilder items)
    {
        items.MapPut("/{itemId:guid}/categories/{categoryId:guid}", Assign)
            .WithName("AssignCategory")
            .RequireAuthorization(CatalogModule.ManagePolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
        items.MapDelete("/{itemId:guid}/categories/{categoryId:guid}", Remove)
            .WithName("RemoveCategory")
            .RequireAuthorization(CatalogModule.ManagePolicy)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static Task<IResult> Assign(Guid itemId, Guid categoryId, Handler handler, CancellationToken cancellationToken) =>
        Change(itemId, categoryId, assign: true, handler, cancellationToken);

    private static Task<IResult> Remove(Guid itemId, Guid categoryId, Handler handler, CancellationToken cancellationToken) =>
        Change(itemId, categoryId, assign: false, handler, cancellationToken);

    private static async Task<IResult> Change(Guid itemId, Guid categoryId, bool assign, Handler handler, CancellationToken cancellationToken)
    {
        Outcome outcome = await handler.Handle(itemId, categoryId, assign, cancellationToken);
        return outcome switch
        {
            Outcome.MissingItem => CatalogProblem.MissingItem(),
            Outcome.MissingCategory => CatalogProblem.MissingCategory(),
            _ => Results.NoContent()
        };
    }

    internal enum Outcome { Applied, MissingItem, MissingCategory }

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        private const string AssignSql = """
            INSERT INTO catalog.item_categories (item_id, category_id)
            SELECT i.id, c.id FROM item i CROSS JOIN category c
            ON CONFLICT DO NOTHING
            """;

        private const string RemoveSql = "DELETE FROM catalog.item_categories WHERE item_id = $1 AND category_id = $2";

        public async Task<Outcome> Handle(Guid itemId, Guid categoryId, bool assign, CancellationToken cancellationToken)
        {
            // Check both parents and change membership against one PostgreSQL statement snapshot.
            string change = assign ? AssignSql : RemoveSql;
            await using NpgsqlCommand command = database.CreateCommand($"""
                WITH item AS (SELECT id FROM catalog.items WHERE id = $1),
                     category AS (SELECT id FROM catalog.categories WHERE id = $2),
                     change AS ({change})
                SELECT EXISTS (SELECT 1 FROM item), EXISTS (SELECT 1 FROM category)
                """);
            command.Parameters.AddWithValue(itemId);
            command.Parameters.AddWithValue(categoryId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (!reader.GetBoolean(0))
            {
                return Outcome.MissingItem;
            }

            return reader.GetBoolean(1) ? Outcome.Applied : Outcome.MissingCategory;
        }
    }
}
