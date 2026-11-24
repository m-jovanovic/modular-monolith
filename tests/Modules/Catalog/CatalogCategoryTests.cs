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
    public async Task ClassificationsAreRepeatableAndConstrainPublicSearch()
    {
        using HttpResponseMessage created = await app.Staff.PostAsJsonAsync(Categories, new { name = $"Power tools {Guid.NewGuid():N}" });
        JsonElement category = await created.Content.ReadFromJsonAsync<JsonElement>();
        Guid categoryId = category.GetProperty("id").GetGuid();
        using HttpResponseMessage item = await app.Staff.PostAsJsonAsync("/api/catalog/items", new { title = "Angle grinder", description = "115 mm disc." });
        string location = item.Headers.Location!.ToString();
        string membership = $"{location}/categories/{categoryId}";

        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpResponseMessage assigned = await app.Staff.PutAsync(membership, null);
            Assert.Equal(HttpStatusCode.NoContent, assigned.StatusCode);
        }

        JsonElement classifications = await app.Public.GetFromJsonAsync<JsonElement>(location + "/categories");
        Assert.Equal(categoryId, Assert.Single(classifications.EnumerateArray()).GetProperty("id").GetGuid());
        string search = $"/api/catalog/items?categoryId={categoryId}";
        JsonElement matches = await app.Public.GetFromJsonAsync<JsonElement>(search);
        Assert.Equal("Angle grinder", Assert.Single(matches.EnumerateArray()).GetProperty("title").GetString());
        using HttpResponseMessage removed = await app.Staff.DeleteAsync(membership);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using HttpResponseMessage repeated = await app.Staff.DeleteAsync(membership);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Empty((await app.Public.GetFromJsonAsync<JsonElement>(search)).EnumerateArray());
        Assert.Empty((await app.Public.GetFromJsonAsync<JsonElement>(location + "/categories")).EnumerateArray());
        using HttpResponseMessage missingItem = await app.Public.GetAsync($"/api/catalog/items/{Guid.NewGuid()}/categories");
        Assert.Equal(HttpStatusCode.NotFound, missingItem.StatusCode);
        using HttpResponseMessage unknown = await app.Staff.PutAsync($"{location}/categories/{Guid.NewGuid()}", null);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

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
