using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EquipmentLibrary.Catalog.Tests;

[Collection(nameof(CatalogFixture))]
public sealed class CatalogCategoryTests(CatalogFixture app)
{
    private const string Categories = "/api/catalog/categories";

    [Fact]
    public async Task ConcurrentCaseVariantsProduceOneCategory()
    {
        string name = $"Woodworking {Guid.NewGuid():N}";
        using var firstRequest = new HttpRequestMessage(HttpMethod.Post, Categories) { Content = JsonContent.Create(new { name }) };
        using var secondRequest = new HttpRequestMessage(HttpMethod.Post, Categories)
        {
            Content = JsonContent.Create(new { name = $" {name.ToUpperInvariant()} " })
        };
        HttpResponseMessage[] results = await Task.WhenAll(app.Staff.SendAsync(firstRequest), app.Staff.SendAsync(secondRequest));
        using HttpResponseMessage created = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        using HttpResponseMessage conflict = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);

        JsonElement category = await app.Public.GetFromJsonAsync<JsonElement>(created.Headers.Location);
        string? categoryName = category.GetProperty("name").GetString();
        Assert.Equal(name, categoryName, ignoreCase: true);
        JsonElement index = await app.Public.GetFromJsonAsync<JsonElement>(Categories);
        Assert.Single(index.EnumerateArray(), item => item.GetProperty("name").GetString() == categoryName);
        Assert.Equal("category-name-in-use", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
