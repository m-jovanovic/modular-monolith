using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace EquipmentLibrary.Api.Bootstrap;

internal static class ApiComposition
{
    internal static void AddApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddLibraryOpenApi();
    }

    internal static void MapApi(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/ready");
        app.MapOpenApi();
    }
}
