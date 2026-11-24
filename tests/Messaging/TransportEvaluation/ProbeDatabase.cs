using Npgsql;

namespace EquipmentLibrary.TransportEvaluation;

internal sealed class ProbeDatabase(NpgsqlDataSource server, string connectionString) : IAsyncDisposable
{
    private readonly string _id = Guid.NewGuid().ToString("N");
    internal string Name => $"transport_{_id}";
    internal string LendingRole => $"wl_{_id}";
    internal string PickupRole => $"wp_{_id}";
    internal string Admin => new NpgsqlConnectionStringBuilder(connectionString) { Database = Name, Pooling = false }.ConnectionString;
    internal string Lending => Credentials(LendingRole);
    internal string Pickup => Credentials(PickupRole);
    private string Password { get; } = Guid.NewGuid().ToString("N");

    internal static async Task<ProbeDatabase> CreateAsync(NpgsqlDataSource server, string connectionString)
    {
        var db = new ProbeDatabase(server, connectionString);
        try
        {
            await using (NpgsqlCommand create = server.CreateCommand($"CREATE DATABASE {db.Name}"))
            {
                await create.ExecuteNonQueryAsync();
            }

            await ExecuteAsync(db.Admin, $"""
                CREATE ROLE {db.LendingRole} LOGIN PASSWORD '{db.Password}';
                CREATE ROLE {db.PickupRole} LOGIN PASSWORD '{db.Password}';
                REVOKE CREATE ON SCHEMA public FROM PUBLIC;
                CREATE SCHEMA lending;
                CREATE SCHEMA pickup_desk;
                GRANT USAGE ON SCHEMA lending TO {db.LendingRole};
                GRANT USAGE ON SCHEMA pickup_desk TO {db.PickupRole};
                CREATE TABLE lending.holds (id uuid PRIMARY KEY);
                CREATE TABLE pickup_desk.projected_holds (id uuid PRIMARY KEY);
                """);
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    internal async Task GrantRuntimeAsync()
    {
        await ExecuteAsync(Admin, $"""
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA lending TO {LendingRole};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA lending TO {LendingRole};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA pickup_desk TO {PickupRole};
            GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA pickup_desk TO {PickupRole};
            """);
    }

    private string Credentials(string role) =>
        new NpgsqlConnectionStringBuilder(Admin) { Username = role, Password = Password }.ConnectionString;

    internal static async Task ExecuteAsync(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using NpgsqlCommand query = db.CreateCommand();
        query.CommandText = sql;
        await query.ExecuteNonQueryAsync();
    }

    internal static async Task<long> CountAsync(string connection, string sql)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync();
        await using NpgsqlCommand query = db.CreateCommand();
        query.CommandText = sql;
        return (long)(await query.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        await using NpgsqlCommand drop = server.CreateCommand($"DROP DATABASE IF EXISTS {Name} WITH (FORCE)");
        await drop.ExecuteNonQueryAsync();
        await using NpgsqlCommand roles = server.CreateCommand($"DROP ROLE IF EXISTS {LendingRole}, {PickupRole}");
        await roles.ExecuteNonQueryAsync();
    }
}
