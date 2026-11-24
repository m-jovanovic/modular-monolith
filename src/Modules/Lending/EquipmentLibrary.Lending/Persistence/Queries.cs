using Npgsql;

namespace EquipmentLibrary.Lending.Persistence;

internal static class Queries
{
    internal static NpgsqlCommand Bind(this NpgsqlCommand command, params object[] values)
    {
        foreach (object value in values)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = value });
        }

        return command;
    }

    internal static async Task<T?> ReadOneAsync<T>(
        this NpgsqlCommand command, Func<NpgsqlDataReader, T> map, CancellationToken cancellationToken)
        where T : class
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? map(reader) : null;
    }

    internal static async Task<List<T>> ReadListAsync<T>(
        this NpgsqlCommand command, Func<NpgsqlDataReader, T> map, CancellationToken cancellationToken)
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<T>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(map(reader));
        }

        return rows;
    }
}
