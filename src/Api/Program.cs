using EquipmentLibrary.Api.Bootstrap;
using EquipmentLibrary.Api.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddLibraryAuthentication();
builder.Services.AddApi();
builder.AddLibraryModules();

WebApplication app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages(ApiProblems.WriteAsync);
app.UseAuthentication();
app.UseAuthorization();
app.MapApi();
app.MapLibraryModules();
app.Run();

public partial class Program;
