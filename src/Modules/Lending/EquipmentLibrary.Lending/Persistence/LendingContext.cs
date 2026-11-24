using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EquipmentLibrary.Lending.Persistence;

internal sealed class LendingContext(
    [FromKeyedServices("Lending")] NpgsqlDataSource dataSource,
    TimeProvider clock)
{
    public NpgsqlDataSource DataSource { get; } = dataSource;
    public TimeProvider Clock { get; } = clock;
}
