using EquipmentLibrary.Catalog.Contracts;
using NpgsqlTypes;

namespace EquipmentLibrary.Catalog.Items;

internal static class SearchItems
{
    internal static void MapEndpoints(RouteGroupBuilder items)
    {
        items.MapGet("/", Search).AllowAnonymous();
        items.MapGet("/archived", SearchArchive).RequireAuthorization(CatalogModule.ManagePolicy);
    }

    private static Task<IResult> Search(string? q, Guid? categoryId, int? page, int? pageSize, Handler handler, CancellationToken cancellationToken) =>
        Find(q, categoryId, page, pageSize, archived: false, handler, cancellationToken);

    private static Task<IResult> SearchArchive(string? q, Guid? categoryId, int? page, int? pageSize, Handler handler, CancellationToken cancellationToken) =>
        Find(q, categoryId, page, pageSize, archived: true, handler, cancellationToken);

    private static async Task<IResult> Find(string? q, Guid? categoryId, int? page, int? pageSize, bool archived, Handler handler, CancellationToken cancellationToken)
    {
        string term = q?.Trim() ?? string.Empty;
        int number = page ?? 1;
        int size = pageSize ?? 25;
        if (term.Length > 160 || term.Contains('\0') || number is < 1 or > 100_000 || size is < 1 or > 100)
        {
            return CatalogProblem.Invalid("Use a search term up to 160 characters, a page from 1 to 100000 and a pageSize from 1 to 100.");
        }

        IReadOnlyList<CatalogItemDescription> matches = await handler.Handle(term, categoryId, number, size, archived, cancellationToken);
        return Results.Ok(matches);
    }

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        public async Task<IReadOnlyList<CatalogItemDescription>> Handle(string term, Guid? categoryId, int page, int pageSize, bool archived, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand query = database.CreateCommand("""
                SELECT id, title, description
                FROM catalog.items i
                WHERE strpos(lower(title || ' ' || description), lower($1)) > 0 AND is_archived = $4
                  AND ($5::uuid IS NULL OR EXISTS (
                    SELECT 1 FROM catalog.item_categories ic WHERE ic.item_id = i.id AND ic.category_id = $5
                  ))
                ORDER BY lower(title), id
                LIMIT $2 OFFSET $3
                """);
            query.Parameters.AddWithValue(term);
            query.Parameters.AddWithValue(pageSize);
            query.Parameters.AddWithValue((page - 1) * pageSize);
            query.Parameters.AddWithValue(archived);
            query.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Uuid, Value = (object?)categoryId ?? DBNull.Value });
            await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(cancellationToken);
            var matches = new List<CatalogItemDescription>();
            while (await reader.ReadAsync(cancellationToken))
            {
                matches.Add(new CatalogItemDescription(reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));
            }

            return matches;
        }
    }
}
