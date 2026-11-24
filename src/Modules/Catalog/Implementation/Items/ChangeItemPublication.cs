namespace EquipmentLibrary.Catalog.Items;

internal static class ChangeItemPublication
{
    internal static void MapEndpoints(RouteGroupBuilder items)
    {
        items.MapPost("/{itemId:guid}/archive", Archive).WithName("ArchiveItem").RequireAuthorization(CatalogModule.ManagePolicy);
        items.MapPost("/{itemId:guid}/restore", Restore).WithName("RestoreItem").RequireAuthorization(CatalogModule.ManagePolicy);
    }

    private static Task<IResult> Archive(Guid itemId, Handler handler, CancellationToken cancellationToken) =>
        SetPublication(itemId, archived: true, handler, cancellationToken);

    private static Task<IResult> Restore(Guid itemId, Handler handler, CancellationToken cancellationToken) =>
        SetPublication(itemId, archived: false, handler, cancellationToken);

    private static async Task<IResult> SetPublication(Guid itemId, bool archived, Handler handler, CancellationToken cancellationToken) =>
        await handler.Handle(itemId, archived, cancellationToken) ? Results.NoContent() : CatalogProblem.MissingItem();

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        public async Task<bool> Handle(Guid itemId, bool archived, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand update = database.CreateCommand(
                "UPDATE catalog.items SET is_archived = $2 WHERE id = $1");
            update.Parameters.AddWithValue(itemId);
            update.Parameters.AddWithValue(archived);
            return await update.ExecuteNonQueryAsync(cancellationToken) == 1;
        }
    }
}
