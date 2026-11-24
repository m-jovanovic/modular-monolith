using EquipmentLibrary.Catalog.Categories;
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
        services.AddScoped<ChangeItemPublication.Handler>();
        services.AddScoped<CreateCategory.Handler>();
        services.AddScoped<BrowseCategories.Handler>();
        services.AddScoped<ClassifyItem.Handler>();
        services.AddScoped<GetItemCategories.Handler>();
        return services;
    }

    public static IEndpointRouteBuilder MapCatalog(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder items = endpoints.MapGroup("/api/catalog/items").WithTags("Catalog");
        CreateItem.MapEndpoint(items);
        GetItem.MapEndpoint(items);
        UpdateItem.MapEndpoint(items);
        SearchItems.MapEndpoints(items);
        ChangeItemPublication.MapEndpoints(items);
        RouteGroupBuilder categories = endpoints.MapGroup("/api/catalog/categories").WithTags("Catalog");
        CreateCategory.MapEndpoint(categories);
        BrowseCategories.MapEndpoints(categories);
        ClassifyItem.MapEndpoints(items);
        GetItemCategories.MapEndpoint(items);
        return endpoints;
    }
}
