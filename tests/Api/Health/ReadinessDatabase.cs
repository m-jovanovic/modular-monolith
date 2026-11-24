using Npgsql;
using Testcontainers.PostgreSql;

namespace EquipmentLibrary.Api.Tests.Health;

internal sealed class ReadinessDatabase : IAsyncDisposable
{
    internal static IReadOnlyList<string> Modules { get; } = ["Catalog", "Lending", "PickupDesk"];

    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly string _password = Guid.NewGuid().ToString("N");
    private PostgreSqlContainer? _postgres;
    private NpgsqlDataSource? _server;
    private NpgsqlDataSource? _admin;
    private string _databaseConnection = "";
    private string DatabaseName => $"ready_{_id}";

    private static string Schema(string module) => module switch
    {
        "Catalog" => "catalog",
        "Lending" => "lending",
        "PickupDesk" => "pickup_desk",
        _ => throw new ArgumentOutOfRangeException(nameof(module))
    };

    internal async Task InitializeAsync()
    {
        string? serverConnection = Environment.GetEnvironmentVariable("EQLIB_TEST_PG_ADMIN");
        if (string.IsNullOrWhiteSpace(serverConnection))
        {
            _postgres = new PostgreSqlBuilder("postgres:17.12-alpine3.23").Build();
            await _postgres.StartAsync();
            serverConnection = _postgres.GetConnectionString();
        }

        _server = NpgsqlDataSource.Create(serverConnection);
        await using NpgsqlCommand create = _server.CreateCommand($"CREATE DATABASE {DatabaseName}");
        await create.ExecuteNonQueryAsync();
        _databaseConnection = new NpgsqlConnectionStringBuilder(serverConnection) { Database = DatabaseName }.ConnectionString;
        _admin = NpgsqlDataSource.Create(_databaseConnection);
        foreach (string module in Modules)
        {
            string schema = Schema(module);
            await using NpgsqlCommand setup = _admin.CreateCommand($"""
                CREATE ROLE {schema}_{_id} LOGIN PASSWORD '{_password}';
                CREATE SCHEMA {schema};
                GRANT USAGE ON SCHEMA {schema} TO {schema}_{_id};
                ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT SELECT ON TABLES TO {schema}_{_id};
                """);
            await setup.ExecuteNonQueryAsync();
        }
    }

    internal string RuntimeConnection(string module) =>
        new NpgsqlConnectionStringBuilder(_databaseConnection) { Username = $"{Schema(module)}_{_id}", Password = _password }.ConnectionString;

    internal async Task MigrateAsync(string module, int minimum, int maximum)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, module, "Migrations");
        foreach (string path in Directory.GetFiles(directory, "*.sql").Order(StringComparer.Ordinal))
        {
            int number = int.Parse(Path.GetFileName(path).Split('_')[0], System.Globalization.CultureInfo.InvariantCulture);
            if (number < minimum || number > maximum) continue;
            await using NpgsqlCommand command = _admin!.CreateCommand(await File.ReadAllTextAsync(path));
            await command.ExecuteNonQueryAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_admin is not null) await _admin.DisposeAsync();
        if (_server is not null)
        {
            await using NpgsqlCommand drop = _server.CreateCommand($"DROP DATABASE IF EXISTS {DatabaseName} WITH (FORCE)");
            await drop.ExecuteNonQueryAsync();
            await using NpgsqlCommand roles = _server.CreateCommand($"DROP ROLE IF EXISTS catalog_{_id}, lending_{_id}, pickup_desk_{_id}");
            await roles.ExecuteNonQueryAsync();
            await _server.DisposeAsync();
        }

        if (_postgres is not null) await _postgres.DisposeAsync();
    }
}
