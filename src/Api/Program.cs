using EquipmentLibrary.Api.Bootstrap;

namespace EquipmentLibrary.Api;

public sealed class Program
{
    private Program()
    {
    }

    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Services.AddApi();

        WebApplication app = builder.Build();
        app.UseExceptionHandler();
        app.MapApi();
        app.Run();
    }
}
