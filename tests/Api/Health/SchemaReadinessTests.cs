using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EquipmentLibrary.Api.Tests.Health;

public sealed class SchemaReadinessTests
{
    [Theory]
    [InlineData("Lending", "PickupDesk")]
    [InlineData("PickupDesk", "Lending")]
    public async Task HealthStaysLiveWhileReadinessWaitsForBothModuleUpgrades(string first, string second)
    {
        await using var database = new ReadinessDatabase();
        await database.InitializeAsync();
        await database.MigrateAsync("Catalog", 0, 3);
        await database.MigrateAsync("Lending", 0, 901);
        await database.MigrateAsync("PickupDesk", 0, 2);

        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Workers:Enabled", "false");
            foreach (string module in ReadinessDatabase.Modules)
            {
                host.UseSetting($"ConnectionStrings:{module}", database.RuntimeConnection(module));
            }
        });
        using HttpClient client = factory.CreateClient();
        await AssertHealthAsync(client, HttpStatusCode.ServiceUnavailable);

        await ApplyRemainingAsync(first);
        await AssertHealthAsync(client, HttpStatusCode.ServiceUnavailable);

        await ApplyRemainingAsync(second);
        await AssertHealthAsync(client, HttpStatusCode.OK);

        Task ApplyRemainingAsync(string module) =>
            database.MigrateAsync(module, module == "Lending" ? 902 : 3, int.MaxValue);
    }

    private static async Task AssertHealthAsync(HttpClient client, HttpStatusCode readiness)
    {
        using HttpResponseMessage live = await client.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using HttpResponseMessage ready = await client.GetAsync(new Uri("/ready", UriKind.Relative));
        Assert.Equal(readiness, ready.StatusCode);
    }
}
