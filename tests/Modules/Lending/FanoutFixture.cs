using EquipmentLibrary.Lending.Delivery;
using EquipmentLibrary.Lending.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.Lending.Tests;

public sealed class FanoutFixture : LendingFixture
{
    internal DurableReceiver Circulation { get; private set; } = null!;
    internal DurableReceiver Inventory { get; private set; } = null!;
    internal IHoldDispatcher Dispatcher => Services.GetRequiredService<IHoldDispatcher>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        Circulation = new DurableReceiver("circulation-summary.v1", Admin);
        Inventory = new DurableReceiver("inventory-history.v1", Admin);
        services.AddSingleton<IHoldSubscriber>(Inventory);
        services.AddSingleton<IHoldSubscriber>(Circulation);
        services.AddHoldDelivery(runWorker: false);
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await using NpgsqlCommand create = Admin.CreateCommand("""
            CREATE SCHEMA receiver_test;
            CREATE TABLE receiver_test.attempts (subscriber text NOT NULL, message_id uuid NOT NULL);
            CREATE TABLE receiver_test.receipts (subscriber text, message_id uuid, PRIMARY KEY (subscriber, message_id));
            CREATE TABLE receiver_test.totals (subscriber text, hold_id uuid, effects int NOT NULL, PRIMARY KEY (subscriber, hold_id));
            """);
        await create.ExecuteNonQueryAsync();
    }
}

internal sealed class DurableReceiver(string name, NpgsqlDataSource database) : IHoldSubscriber
{
    private Func<CancellationToken, Task>? _nextAction;
    public string Name => name;

    internal void OnNextAttempt(Func<CancellationToken, Task> action) => Interlocked.Exchange(ref _nextAction, action);

    public async Task AcceptAsync(HoldLifecycleEvent message, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand attempt = database.CreateCommand("INSERT INTO receiver_test.attempts VALUES ($1, $2)");
        attempt.Parameters.AddWithValue(Name);
        attempt.Parameters.AddWithValue(message.MessageId);
        await attempt.ExecuteNonQueryAsync(cancellationToken);
        if (Interlocked.Exchange(ref _nextAction, null) is { } action)
        {
            await action(cancellationToken);
        }

        await using NpgsqlCommand apply = database.CreateCommand("""
            WITH accepted AS (
                INSERT INTO receiver_test.receipts VALUES ($1, $2) ON CONFLICT DO NOTHING RETURNING subscriber
            )
            INSERT INTO receiver_test.totals SELECT subscriber, $3, 1 FROM accepted
            ON CONFLICT (subscriber, hold_id) DO UPDATE SET effects = receiver_test.totals.effects + 1
            """);
        apply.Parameters.AddWithValue(Name);
        apply.Parameters.AddWithValue(message.MessageId);
        apply.Parameters.AddWithValue(message.Data.HoldId);
        await apply.ExecuteNonQueryAsync(cancellationToken);
    }
}
