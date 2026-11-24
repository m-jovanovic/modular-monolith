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
        using JsonDocument document = await ReadDocumentAsync();
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
        JsonElement place = paths.GetProperty("/api/lending/holds").GetProperty("post");
        Assert.Contains("Borrower", place.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.True(place.GetProperty("security")[0].TryGetProperty("LibraryBearer", out _));
        JsonElement browse = paths.GetProperty("/api/catalog/items").GetProperty("get");
        Assert.False(browse.TryGetProperty("security", out _));
        Assert.True(paths.GetProperty("/api/pickup-desk/queue").TryGetProperty("get", out _));
    }

    [Fact]
    public async Task RequestSchemasContainTheFieldsEachFeatureActuallyAccepts()
    {
        using JsonDocument document = await ReadDocumentAsync();
        var fields = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CreateItem"] = ["description", "title"],
            ["UpdateItem"] = ["description", "title"],
            ["CreateCategory"] = ["name"],
            ["EnrollCopy"] = ["barcode", "catalogItemId"],
            ["PlaceHold"] = ["copyId"],
            ["OpenMaintenanceOrder"] = ["reason"],
            ["RepairCopy"] = ["note"],
            ["WithdrawDamagedCopy"] = ["note"],
            ["ReportPickupIssue"] = ["reason"],
            ["RedriveReceipt"] = ["reason"]
        };

        foreach (JsonElement operation in Operations(document))
        {
            if (!operation.TryGetProperty("requestBody", out JsonElement body))
            {
                continue;
            }

            string id = operation.GetProperty("operationId").GetString()!;
            Assert.True(fields.Remove(id, out string[]? expected), $"Missing request fixture for {id}.");
            JsonElement schema = Resolve(document, body.GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            string[] actual = schema.GetProperty("properties").EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray();
            Assert.True(expected.SequenceEqual(actual), $"{id} expects {string.Join(", ", expected)} but advertises {string.Join(", ", actual)}.");
        }

        Assert.Empty(fields);
    }

    private static IEnumerable<JsonElement> Operations(JsonDocument document) =>
        document.RootElement.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject())
            .Select(method => method.Value)
            .Where(value => value.TryGetProperty("operationId", out _));

    private static JsonElement Resolve(JsonDocument document, JsonElement schema)
    {
        if (!schema.TryGetProperty("$ref", out JsonElement reference))
        {
            return schema;
        }

        const string prefix = "#/components/schemas/";
        string target = reference.GetString()!;
        Assert.StartsWith(prefix, target, StringComparison.Ordinal);
        return document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(target[prefix.Length..]);
    }

    private static async Task<JsonDocument> ReadDocumentAsync()
    {
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host => host.UseSetting("Workers:Enabled", "false"));
        using HttpClient client = factory.CreateClient();
        return await client.GetFromJsonAsync<JsonDocument>(
            new Uri("/openapi/v1.json", UriKind.Relative)) ?? throw new InvalidDataException("Missing API document.");
    }
}
