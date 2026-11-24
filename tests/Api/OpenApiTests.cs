using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace EquipmentLibrary.Api.Tests;

public sealed class OpenApiTests
{
    [Fact]
    public async Task PublishedApiIncludesTheBorrowerJourneyAcrossAllThreeModules()
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host.UseSetting("Workers:Enabled", "false"));
        using HttpClient client = factory.CreateClient();
        using JsonDocument document = await client.GetFromJsonAsync<JsonDocument>(
            new Uri("/openapi/v1.json", UriKind.Relative)) ?? throw new InvalidDataException("Missing API document.");
        JsonElement paths = document.RootElement.GetProperty("paths");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty path in paths.EnumerateObject().Where(p => p.Name.StartsWith("/api/", StringComparison.Ordinal)))
        {
            foreach (JsonProperty method in path.Value.EnumerateObject())
            {
                if (method.Name is "parameters") continue;
                Assert.True(names.Add(method.Value.GetProperty("operationId").GetString()!), $"Duplicate operation on {path.Name}.");
                Assert.NotEmpty(method.Value.GetProperty("tags").EnumerateArray());
            }
        }

        Assert.True(paths.GetProperty("/api/catalog/items").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/lending/holds").TryGetProperty("post", out _));
        Assert.True(paths.GetProperty("/api/pickup-desk/queue").TryGetProperty("get", out _));
    }
}
