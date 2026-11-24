namespace EquipmentLibrary.Api.Bootstrap;

internal static class ApiComposition
{
    internal static void AddApi(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddHealthChecks();
    }

    internal static void MapApi(this WebApplication app)
    {
        app.MapHealthChecks("/health");
    }
}
