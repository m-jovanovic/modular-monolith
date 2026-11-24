using EquipmentLibrary.Api.Bootstrap;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.AddApi();

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.MapApi();
app.Run();

public partial class Program;
