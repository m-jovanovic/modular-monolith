using EquipmentLibrary.Catalog;
using EquipmentLibrary.Lending;
using EquipmentLibrary.Lending.Delivery;
using EquipmentLibrary.PickupDesk;
using Npgsql;

namespace EquipmentLibrary.Api.Bootstrap;

internal static class Modules
{
    internal static void AddLibraryModules(this WebApplicationBuilder builder)
    {
        foreach (string module in new[] { "Catalog", "Lending", "PickupDesk" })
        {
            builder.Services.AddKeyedSingleton<NpgsqlDataSource>(module, (_, _) =>
                NpgsqlDataSource.Create(builder.Configuration.GetConnectionString(module)
                    ?? throw new InvalidOperationException($"Configure ConnectionStrings:{module}.")));
        }

        builder.Services.AddSingleton(TimeProvider.System);
        bool workersEnabled = builder.Configuration.GetValue("Workers:Enabled", true);
        builder.Services.AddCatalog().AddLending(workersEnabled).AddPickupDesk(workersEnabled);
        builder.Services.AddSingleton<IHoldSubscriber, PickupSubscription>();
        builder.Services.AddHoldDelivery(workersEnabled);
    }

    internal static void MapLibraryModules(this WebApplication app)
    {
        app.MapCatalog().MapLending().MapPickupDesk();
        app.MapDeliveryOperations();
    }
}
