using Npgsql;

namespace EquipmentLibrary.Lending.Persistence;

internal static class Transactions
{
    internal static NpgsqlCommand Sql(this NpgsqlConnection connection, string sql, params object[] values) =>
        new NpgsqlCommand(sql, connection).Bind(values);
}
