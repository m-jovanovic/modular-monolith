using EquipmentLibrary.Api.Identity;
using EquipmentLibrary.Catalog;
using EquipmentLibrary.Lab.JetStream;
using EquipmentLibrary.Lab.Publisher;
using EquipmentLibrary.Lending;
using EquipmentLibrary.Lending.Delivery;
using NATS.Client.JetStream;
using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddLibraryAuthentication();
builder.Services.AddSingleton(TimeProvider.System);
foreach (string module in new[] { "Catalog", "Lending" })
{
    builder.Services.AddKeyedSingleton<NpgsqlDataSource>(module, (_, _) =>
        NpgsqlDataSource.Create(builder.Configuration.GetConnectionString(module) ?? throw new InvalidOperationException(module)));
}

builder.Services.AddCatalog().AddLending().AddHoldDelivery(runWorker: true);
builder.Services.AddHoldStream(builder.Configuration["BrokerUrl"] ?? throw new InvalidOperationException("Set BrokerUrl."));
builder.Services.AddSingleton<IHoldSubscriber, JetStreamSubscription>();

WebApplication app = builder.Build();
await app.Services.GetRequiredService<NatsJSContext>().PrepareAsync();
app.UseAuthentication();
app.UseAuthorization();
app.MapCatalog().MapLending();
app.MapGet("/health", () => Results.Ok());
await app.RunAsync();
