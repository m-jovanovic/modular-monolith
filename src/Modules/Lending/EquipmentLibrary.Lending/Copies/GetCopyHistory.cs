using EquipmentLibrary.Lending.Persistence;
using Microsoft.AspNetCore.Http;
using Npgsql;

namespace EquipmentLibrary.Lending.Copies;

internal static class GetCopyHistory
{
    internal sealed record Entry(Guid HoldId, string State, DateTimeOffset PlacedAt, DateTimeOffset? ClosedAt, DateTimeOffset? ReturnedAt);

    internal static async Task<IResult> HandleAsync(Guid copyId, int? limit, int? offset, LendingContext lending, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 100 || offset is < 0)
        {
            return LendingErrors.Invalid("Use a nonnegative offset and limit between 1 and 100.");
        }

        await using NpgsqlCommand exists = lending.DataSource.CreateCommand("SELECT 1 FROM lending.copies WHERE id = $1").Bind(copyId);
        if (await exists.ExecuteScalarAsync(cancellationToken) is null)
        {
            return LendingErrors.CopyNotFound();
        }

        await using NpgsqlCommand history = lending.DataSource.CreateCommand("""
            SELECT h.id, h.state, h.placed_at, h.closed_at, l.returned_at
            FROM lending.holds h LEFT JOIN lending.loans l ON l.id = h.id
            WHERE h.copy_id = $1 ORDER BY h.placed_at DESC, h.id LIMIT $2 OFFSET $3
            """).Bind(copyId, limit ?? 25, offset ?? 0);
        List<Entry> items = await history.ReadListAsync(r => new Entry(r.GetGuid(0), r.GetString(1),
            r.GetFieldValue<DateTimeOffset>(2), r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3),
            r.IsDBNull(4) ? null : r.GetFieldValue<DateTimeOffset>(4)), cancellationToken);
        return Results.Ok(new LendingPage<Entry>(items));
    }
}
