using EquipmentLibrary.Catalog.Contracts;
using EquipmentLibrary.Catalog.Items;

namespace EquipmentLibrary.Catalog;

public static class CatalogModule
{
    internal const string DatabaseKey = "Catalog";
    internal const string ManagePolicy = "Catalog.Manage";

    public static IServiceCollection AddCatalog(this IServiceCollection services)
    {
        services.AddScoped<ICatalogLookup, CatalogLookup>();
        services.AddScoped<CreateItem.Handler>();
        services.AddScoped<UpdateItem.Handler>();
        services.AddScoped<SearchItems.Handler>();
        return services;
    }

    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder items = endpoints.MapGroup("/api/catalog/items");
        CreateItem.MapEndpoint(items);
        GetItem.MapEndpoint(items);
        UpdateItem.MapEndpoint(items);
        SearchItems.MapEndpoint(items);
        return endpoints;
    }
}
