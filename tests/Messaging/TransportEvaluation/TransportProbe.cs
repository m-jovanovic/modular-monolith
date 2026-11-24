using System.Diagnostics;
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

        using (IHost setup = BuildHost(db.Admin, provision: true, receiver: true))
        {
            await setup.StartAsync();
            await setup.StopAsync();
        }

        await db.GrantRuntimeAsync();
        using (IHost host = BuildHost(db.Lending, provision: false))
        {
            await host.StartAsync();
            IWolverineRuntime runtime = host.Services.GetRequiredService<IWolverineRuntime>();
            await WriteHoldAsync(db.Lending, runtime, commit: false);
            await ExpectCountsAsync(db, 0);
            await host.StopAsync();
        }

        await InterruptPublisherAsync(db.Lending);
        await ExpectCountsAsync(db, 1);
        await ExpectReceiverDenialAsync(db);
        if (await ProbeDatabase.CountAsync(db.Pickup, "SELECT count(*) FROM pickup_desk.projected_holds") != 0)
        {
            throw new InvalidOperationException("A denied listener applied a pickup.");
        }
    }

    private static async Task ExpectReceiverDenialAsync(ProbeDatabase db)
    {
        using var errors = new ReceiverDiagnostics();
        using IHost host = BuildHost(db.Pickup, provision: false, receiver: true, diagnostics: errors);
        try
        {
            await host.StartAsync();
            await errors.Denied.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        catch (Exception ex) when (ReceiverDiagnostics.IsSchemaDenial(ex))
        {
            errors.Denied.TrySetResult();
        }
        finally
        {
            await host.StopAsync();
        }

        if (!errors.Denied.Task.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException("Expected the PickupDesk listener to report insufficient privilege.");
        }

        Console.WriteLine("PickupDesk Wolverine listener on the shared Lending queue: PostgreSQL 42501.");
    }

    internal static async Task PublishThenWaitAsync(string connectionString)
    {
        using IHost host = BuildHost(connectionString, provision: false);
        await host.StartAsync();
        await WriteHoldAsync(connectionString, host.Services.GetRequiredService<IWolverineRuntime>(), commit: true);
        // The parent kills this process after commit, before FlushOutgoingMessagesAsync.
        Console.WriteLine("COMMITTED");
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private static async Task InterruptPublisherAsync(string connectionString)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true };
        start.ArgumentList.Add(typeof(TransportProbe).Assembly.Location);
        start.ArgumentList.Add("publisher");
        start.Environment.Remove("EQLIB_TEST_PG_ADMIN");
        start.Environment["EQLIB_PROBE_PUBLISHER"] = connectionString;
        using Process publisher = Process.Start(start) ?? throw new InvalidOperationException("Publisher did not start.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            string? line;
            do
            {
                line = await publisher.StandardOutput.ReadLineAsync(deadline.Token);
                if (line is null) throw new InvalidOperationException("Publisher exited before committing.");
            }
            while (line != "COMMITTED");
        }
        finally
        {
            if (!publisher.HasExited) publisher.Kill(entireProcessTree: true);
            await publisher.WaitForExitAsync();
        }

        Console.WriteLine("Interrupted the publisher after database commit and before flush.");
    }

    private static IHost BuildHost(string connectionString, bool provision, bool receiver = false, ReceiverDiagnostics? diagnostics = null)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.SetMinimumLevel(LogLevel.Error);
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        if (diagnostics is null) builder.Logging.AddSimpleConsole();
        else builder.Logging.AddProvider(diagnostics);
        builder.UseWolverine(options =>
        {
            options.ServiceName = receiver ? "EquipmentLibraryPickupProbe" : "EquipmentLibraryLendingProbe";
            options.Durability.Mode = DurabilityMode.Solo;
            options.AutoBuildMessageStorageOnStartup = provision ? AutoCreate.CreateOrUpdate : AutoCreate.None;
            options.UsePostgresqlPersistenceAndTransport(connectionString, receiver ? "pickup_desk" : "lending", transportSchema: "lending");
            if (receiver)
            {
                options.Discovery.IncludeType(typeof(HoldRecordedHandler));
                options.ListenToPostgresqlQueue("handoff").UseDurableInbox();
            }
            else options.PublishMessage<HoldRecorded>().ToPostgresqlQueue("handoff").UseDurableOutbox();
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
        long envelopes = await ProbeDatabase.CountAsync(db.Lending, "SELECT count(*) FROM lending.wolverine_outgoing_envelopes");
        long queued = await ProbeDatabase.CountAsync(db.Lending, "SELECT count(*) FROM lending.wolverine_queue_handoff");
        if (holds != expected || envelopes + queued != expected)
        {
            throw new InvalidOperationException($"Expected {expected} row(s) in each store. Observed holds={holds}, outgoing={envelopes}, queued={queued}.");
        }

        Console.WriteLine($"Lending transaction: holds={holds}, outgoing={envelopes}, queued={queued}.");
    }
}
