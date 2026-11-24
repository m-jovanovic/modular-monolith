using EquipmentLibrary.Lending.Delivery;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.Lending.Persistence;

internal sealed class LendingContext(
    [FromKeyedServices("Lending")] NpgsqlDataSource dataSource,
    TimeProvider clock, HoldSubscriptions subscriptions)
{
    public NpgsqlDataSource DataSource { get; } = dataSource;
    public TimeProvider Clock { get; } = clock;
    public HoldSubscriptions Subscriptions { get; } = subscriptions;
}
