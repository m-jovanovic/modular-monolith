using EquipmentLibrary.Api.Bootstrap;
using EquipmentLibrary.Api.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddLibraryAuthentication();
builder.Services.AddApi();

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapApi();
app.Run();

public partial class Program;
