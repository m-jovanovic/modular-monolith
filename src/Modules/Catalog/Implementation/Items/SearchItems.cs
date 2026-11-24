using EquipmentLibrary.Catalog.Contracts;

namespace EquipmentLibrary.Catalog.Items;

internal static class SearchItems
{
    internal static void MapEndpoint(RouteGroupBuilder items) =>
        items.MapGet("/", Search).AllowAnonymous();

    private static async Task<IResult> Search(string? q, int? page, int? pageSize, Handler handler, CancellationToken cancellationToken)
    {
        string term = q?.Trim() ?? string.Empty;
        int number = page ?? 1;
        int size = pageSize ?? 25;
        if (term.Length > 160 || term.Contains('\0') || number is < 1 or > 100_000 || size is < 1 or > 100)
        {
            return CatalogProblem.Invalid("Use a search term up to 160 characters, a page from 1 to 100000 and a pageSize from 1 to 100.");
        }

        IReadOnlyList<CatalogItemDescription> matches = await handler.Handle(term, number, size, cancellationToken);
        return Results.Ok(matches);
    }

    internal sealed class Handler([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database)
    {
        public async Task<IReadOnlyList<CatalogItemDescription>> Handle(string term, int page, int pageSize, CancellationToken cancellationToken)
        {
            await using NpgsqlCommand query = database.CreateCommand("""
                SELECT id, title, description
                FROM catalog.items
                WHERE strpos(lower(title || ' ' || description), lower($1)) > 0
                ORDER BY lower(title), id
                LIMIT $2 OFFSET $3
                """);
            query.Parameters.AddWithValue(term);
            query.Parameters.AddWithValue(pageSize);
            query.Parameters.AddWithValue((page - 1) * pageSize);
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
