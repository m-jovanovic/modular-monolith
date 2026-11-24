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

    [Fact]
    public async Task PublishedResponsesDistinguishJsonBodiesFromEmptySuccessesAndProblems()
    {
        using JsonDocument document = await ReadDocumentAsync();

        foreach (JsonElement operation in Operations(document))
        {
            string id = operation.GetProperty("operationId").GetString()!;
            string[] successCodes = id switch
            {
                "CreateItem" or "CreateCategory" or "PlaceHold" or "OpenMaintenanceOrder" => ["201"],
                "EnrollCopy" => ["200", "201"],
                "ArchiveItem" or "RestoreItem" or "AssignCategory" or "RemoveCategory" or
                    "ClaimPickup" or "ReleasePickup" or "MarkPickupReady" or "ReportPickupIssue" or
                    "ResolvePickupIssue" or "RedriveReceipt" or "RedriveDelivery" => ["204"],
                _ => ["200"]
            };
            JsonElement responses = operation.GetProperty("responses");
            string[] actual = responses.EnumerateObject().Select(response => response.Name)
                .Where(code => code.StartsWith('2')).Order(StringComparer.Ordinal).ToArray();
            Assert.True(successCodes.SequenceEqual(actual), $"Unexpected success codes for {id}: {string.Join(", ", actual)}.");

            foreach (string code in actual)
            {
                JsonElement response = responses.GetProperty(code);
                if (code == "204")
                {
                    Assert.False(response.TryGetProperty("content", out _), $"{id} advertises a body for 204.");
                    continue;
                }

                JsonElement schema = Resolve(document, response.GetProperty("content").GetProperty("application/json").GetProperty("schema"));
                if (schema.GetProperty("type").GetString() == "array")
                {
                    schema = Resolve(document, schema.GetProperty("items"));
                }

                JsonElement properties = schema.GetProperty("properties");
                Assert.NotEmpty(properties.EnumerateObject());
                if (properties.TryGetProperty("items", out JsonElement list))
                {
                    Assert.NotEmpty(Resolve(document, list.GetProperty("items")).GetProperty("properties").EnumerateObject());
                }
            }

            if (operation.TryGetProperty("security", out _))
            {
                Assert.True(responses.TryGetProperty("401", out _) && responses.TryGetProperty("403", out _), id);
            }
            foreach (JsonProperty error in responses.EnumerateObject().Where(response => response.Name.StartsWith('4')))
            {
                JsonElement problem = error.Value.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
                Assert.True(Resolve(document, problem).GetProperty("properties").TryGetProperty("status", out _), id);
            }
        }
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
