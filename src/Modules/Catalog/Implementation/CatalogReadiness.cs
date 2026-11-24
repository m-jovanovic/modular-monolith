using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EquipmentLibrary.Catalog;

internal sealed class CatalogReadiness([FromKeyedServices(CatalogModule.DatabaseKey)] NpgsqlDataSource database) : IHealthCheck
{
    private const string Query = """
        SELECT id, title, description, is_archived FROM catalog.items LIMIT 0;
        SELECT id, name FROM catalog.categories LIMIT 0;
        SELECT item_id, category_id FROM catalog.item_categories LIMIT 0;
        """;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = database.CreateCommand(Query);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
