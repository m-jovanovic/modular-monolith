using EquipmentLibrary.TransportEvaluation;
using Npgsql;

if (args is ["publisher"])
{
    string runtimeConnection = Environment.GetEnvironmentVariable("EQLIB_PROBE_PUBLISHER")
        ?? throw new InvalidOperationException("The isolated publisher needs its module credentials.");
    await TransportProbe.PublishThenWaitAsync(runtimeConnection);
    return;
}

string connectionString = Environment.GetEnvironmentVariable("EQLIB_TEST_PG_ADMIN")
    ?? throw new InvalidOperationException("Set EQLIB_TEST_PG_ADMIN to a PostgreSQL admin connection for isolated database creation.");
await using var server = NpgsqlDataSource.Create(connectionString);
await using ProbeDatabase database = await ProbeDatabase.CreateAsync(server, connectionString);
await TransportProbe.RunAsync(database);
