using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using EquipmentLibrary.Lending.Delivery;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

public sealed class SubscriberFanoutTests(FanoutFixture fixture) : IClassFixture<FanoutFixture>
{
    [Fact]
    public async Task DispatchSpanLinksTheHttpTraceToTheStableHoldAndMessageIds()
    {
        var trace = ActivityTraceId.CreateRandom();
        Activity? observed = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "EquipmentLibrary.Lending.Outbox" or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Source.Name == "EquipmentLibrary.Lending.Outbox" && activity.TraceId == trace)
                {
                    observed = activity;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);
        (Guid hold, Guid message) = await ReserveAsync($"00-{trace}-{ActivitySpanId.CreateRandom()}-01");
        Assert.Equal(HoldDispatchOutcome.Delivered, await fixture.Dispatcher.DeliverNextAsync(default));
        Assert.NotNull(observed);
        Assert.Equal(ActivityKind.Producer, observed.Kind);
        Assert.Equal(hold, observed.GetTagItem("library.hold.id"));
        Assert.Equal(message, observed.GetTagItem("messaging.message.id"));
        Assert.Equal("acknowledged", observed.GetTagItem("library.delivery.outcome"));
    }

    [Fact]
    public async Task AFailedSecondSubscriberRetriesTheOriginalMessageAndBothEffectsHappenOnce()
    {
        (Guid hold, Guid message) = await ReserveAsync();
        fixture.Inventory.OnNextAttempt(_ => throw new IOException("Receiver unavailable"));
        Assert.Equal(HoldDispatchOutcome.Failed, await fixture.Dispatcher.DeliverNextAsync(default));
        Assert.Equal(HoldDispatchOutcome.Idle, await fixture.Dispatcher.DeliverNextAsync(default));
        Assert.Equal((1L, 1L, 1), await ReadEffectsAsync(fixture.Circulation.Name, hold, message));
        Assert.Equal((1L, 0L, 0), await ReadEffectsAsync(fixture.Inventory.Name, hold, message));

        await MakeEligibleAsync(message);
        HoldDispatchOutcome[] outcomes = await Task.WhenAll(fixture.Dispatcher.DeliverNextAsync(default), fixture.Dispatcher.DeliverNextAsync(default));
        Assert.Contains(HoldDispatchOutcome.Idle, outcomes);
        Assert.Contains(HoldDispatchOutcome.Delivered, outcomes);
        Assert.Equal((2L, 1L, 1), await ReadEffectsAsync(fixture.Circulation.Name, hold, message));
        Assert.Equal((2L, 1L, 1), await ReadEffectsAsync(fixture.Inventory.Name, hold, message));
        await AssertClosedAsync(message, expectedFailures: 1);
    }

    [Fact]
    public async Task ANewReceiverCannotSubscribeRetroactivelyToACommittedHold()
    {
        (Guid hold, Guid message) = await ReserveAsync();
        var newcomer = new DurableReceiver("waiting-list.v1", fixture.Admin);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddKeyedSingleton("Lending", fixture.Services.GetRequiredKeyedService<NpgsqlDataSource>("Lending"));
        services.AddSingleton<IHoldSubscriber>(newcomer);
        services.AddSingleton<IHoldSubscriber>(fixture.Inventory);
        services.AddSingleton<IHoldSubscriber>(fixture.Circulation);
        services.AddHoldDelivery(runWorker: false);
        await using ServiceProvider nextDeployment = services.BuildServiceProvider();

        IHoldDispatcher dispatcher = nextDeployment.GetRequiredService<IHoldDispatcher>();
        Assert.Equal(HoldDispatchOutcome.Delivered, await dispatcher.DeliverNextAsync(default));
        Assert.Equal((1L, 1L, 1), await ReadEffectsAsync(fixture.Circulation.Name, hold, message));
        Assert.Equal((1L, 1L, 1), await ReadEffectsAsync(fixture.Inventory.Name, hold, message));
        Assert.Equal((0L, 0L, 0), await ReadEffectsAsync(newcomer.Name, hold, message));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnExpiredWorkerCannotAcknowledgeOrFailTheNewOwnersLease(bool staleWorkerFails)
    {
        (Guid hold, Guid message) = await ReserveAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Inventory.OnNextAttempt(async token =>
        {
            entered.SetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            if (staleWorkerFails)
            {
                throw new IOException("Late failure from the original lease");
            }
        });
        Task<HoldDispatchOutcome> firstWorker = fixture.Dispatcher.DeliverNextAsync(default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(HoldDispatchOutcome.Idle, await fixture.Dispatcher.DeliverNextAsync(default));
            await MakeEligibleAsync(message);
            Assert.Equal(HoldDispatchOutcome.Delivered, await fixture.Dispatcher.DeliverNextAsync(default));
        }
        finally
        {
            resume.TrySetResult();
        }

        Assert.Equal(HoldDispatchOutcome.LeaseLost, await firstWorker);
        Assert.Equal((2L, 1L, 1), await ReadEffectsAsync(fixture.Circulation.Name, hold, message));
        Assert.Equal((2L, 1L, 1), await ReadEffectsAsync(fixture.Inventory.Name, hold, message));
        await AssertClosedAsync(message, expectedFailures: 0);
    }

    private async Task<(Guid Hold, Guid Message)> ReserveAsync(string? traceParent = null)
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient borrower = fixture.Client("a");
        if (traceParent is not null)
        {
            borrower.DefaultRequestHeaders.Add("traceparent", traceParent);
        }

        Guid copyId = await ReservationTests.EnrollAsync(staff);
        using HttpResponseMessage response = await borrower.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Guid hold = (await ReservationTests.Json(response)).GetProperty("holdId").GetGuid();
        await using NpgsqlCommand command = fixture.Admin.CreateCommand("""
            SELECT id, subscribers FROM lending.outbox WHERE (payload->'data'->>'holdId')::uuid = $1
            """);
        command.Parameters.AddWithValue(hold);
        await using NpgsqlDataReader row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal([fixture.Circulation.Name, fixture.Inventory.Name], row.GetFieldValue<string[]>(1));
        return (hold, row.GetGuid(0));
    }

    private async Task MakeEligibleAsync(Guid message)
    {
        await using NpgsqlCommand command = fixture.Admin.CreateCommand("UPDATE lending.outbox SET eligible_at = now() WHERE id = $1");
        command.Parameters.AddWithValue(message);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task<(long Attempts, long Receipts, int Effects)> ReadEffectsAsync(string name, Guid hold, Guid message)
    {
        await using NpgsqlCommand command = fixture.Admin.CreateCommand("""
            SELECT (SELECT count(*) FROM receiver_test.attempts WHERE subscriber = $1 AND message_id = $2),
                   (SELECT count(*) FROM receiver_test.receipts WHERE subscriber = $1 AND message_id = $2),
                   coalesce((SELECT effects FROM receiver_test.totals WHERE subscriber = $1 AND hold_id = $3), 0)
            """);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(message);
        command.Parameters.AddWithValue(hold);
        await using NpgsqlDataReader row = await command.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        return (row.GetInt64(0), row.GetInt64(1), row.GetInt32(2));
    }

    private async Task AssertClosedAsync(Guid message, int expectedFailures)
    {
        await using NpgsqlCommand command = fixture.Admin.CreateCommand("""
            SELECT delivered_at IS NOT NULL AND lease_id IS NULL AND last_failure IS NULL AND failure_count = $2
            FROM lending.outbox WHERE id = $1
            """);
        command.Parameters.AddWithValue(message);
        command.Parameters.AddWithValue(expectedFailures);
        Assert.True((bool?)await command.ExecuteScalarAsync());
    }
}
