using JasperFx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Wolverine;
using Wolverine.Postgresql;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace EquipmentLibrary.TransportEvaluation;

public sealed record HoldRecorded(Guid Id);

internal static class TransportProbe
{
    internal static async Task RunAsync(ProbeDatabase db)
    {
        using (IHost setup = BuildHost(db.Admin, provision: true))
        {
            await setup.StartAsync();
            await setup.StopAsync();
        }

        await db.GrantRuntimeAsync();
        using IHost host = BuildHost(db.Lending, provision: false);
        await host.StartAsync();
        IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();
        await WriteHoldAsync(db.Lending, runtime, commit: false);
        await ExpectCountsAsync(db, 0);
        await WriteHoldAsync(db.Lending, runtime, commit: true);
        await ExpectCountsAsync(db, 1);
        try
        {
            await ProbeDatabase.CountAsync(db.Pickup, "SELECT count(*) FROM lending.wolverine_queue_handoff");
            throw new InvalidOperationException("PickupDesk unexpectedly read the sender's transport queue.");
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.InsufficientPrivilege)
        {
            Console.WriteLine($"PickupDesk reading the shared queue in lending: PostgreSQL {e.SqlState}.");
        }

        await host.StopAsync();
    }

    private static IHost BuildHost(string connectionString, bool provision)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.AddSimpleConsole().SetMinimumLevel(LogLevel.Error);
        builder.UseWolverine(options =>
        {
            options.ServiceName = "EquipmentLibraryTransportProbe";
            options.Durability.Mode = DurabilityMode.Solo;
            options.AutoBuildMessageStorageOnStartup = provision ? AutoCreate.CreateOrUpdate : AutoCreate.None;
            options.UsePostgresqlPersistenceAndTransport(connectionString, "lending", transportSchema: "lending");
            options.PublishMessage<HoldRecorded>().ToPostgresqlQueue("handoff").UseDurableOutbox();
        });
        return builder.Build();
    }

    private static async Task WriteHoldAsync(string connectionString, IWolverineRuntime runtime, bool commit)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        var id = Guid.NewGuid();
        await using var hold = new NpgsqlCommand("INSERT INTO lending.holds VALUES ($1)", connection, transaction);
        hold.Parameters.AddWithValue(id);
        await hold.ExecuteNonQueryAsync();
        var context = new MessageContext(runtime);
        using var outbox = new DatabaseEnvelopeTransaction((IMessageDatabase)runtime.Storage, transaction);
        await context.EnlistInOutboxAsync(outbox);
        await context.PublishAsync(new HoldRecorded(id));
        if (commit)
        {
            await transaction.CommitAsync();
        }
        else
        {
            await transaction.RollbackAsync();
        }
    }

    private static async Task ExpectCountsAsync(ProbeDatabase db, long expected)
    {
        long holds = await ProbeDatabase.CountAsync(db.Lending, "SELECT count(*) FROM lending.holds");
        long envelopes = await ProbeDatabase.CountAsync(db.Lending,
            "SELECT (SELECT count(*) FROM lending.wolverine_outgoing_envelopes) + (SELECT count(*) FROM lending.wolverine_queue_handoff)");
        if (holds != expected || envelopes != expected)
        {
            throw new InvalidOperationException($"Expected {expected} row(s) in each store. Observed holds={holds}, messages={envelopes}.");
        }

        Console.WriteLine($"Lending transaction: holds={holds}, messages={envelopes}.");
    }
}
