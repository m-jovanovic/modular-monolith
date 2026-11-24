using EquipmentLibrary.Api.Identity;
using EquipmentLibrary.Lab.JetStream;
using EquipmentLibrary.Lab.PickupService;
using EquipmentLibrary.PickupDesk;
using NATS.Client.JetStream;
using Npgsql;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddLibraryAuthentication();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddKeyedSingleton<NpgsqlDataSource>("PickupDesk", (_, _) =>
    NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("PickupDesk") ?? throw new InvalidOperationException("Set PickupDesk.")));
builder.Services.AddPickupDesk();
builder.Services.AddHoldStream(builder.Configuration["BrokerUrl"] ?? throw new InvalidOperationException("Set BrokerUrl."));
builder.Services.AddHostedService<ReceiveHoldMessages>();

WebApplication app = builder.Build();
await app.Services.GetRequiredService<NatsJSContext>().PrepareAsync();
app.UseAuthentication();
app.UseAuthorization();
app.MapPickupDesk();
app.MapGet("/health", () => Results.Ok());
await app.RunAsync();
