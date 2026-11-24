using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentLibrary.Catalog.Contracts;
using Xunit;

namespace EquipmentLibrary.Catalog.Tests;

public sealed class CatalogItemTests(CatalogFixture app) : IClassFixture<CatalogFixture>
{
    private const string Items = "/api/catalog/items";

    [Fact]
    public async Task StaffPublishesAnItemThatAReaderCanFindById()
    {
        using HttpResponseMessage created = await app.Staff.PostAsJsonAsync(Items,
            new { title = " Cordless drill ", description = "18 V drill with two batteries." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        CatalogItemDescription? item = await created.Content.ReadFromJsonAsync<CatalogItemDescription>();
        Assert.NotNull(item);
        Assert.Equal("Cordless drill", item.Title);
        Assert.Equal(item, await app.Public.GetFromJsonAsync<CatalogItemDescription>(created.Headers.Location));
    }

    [Fact]
    public async Task ReadersSearchLiteralTextInStablePages()
    {
        string term = $"tool%{Guid.NewGuid():N}";
        foreach (string title in new[] { "Plane B", "Plane A", "Plane C" })
        {
            using HttpResponseMessage created = await app.Staff.PostAsJsonAsync(Items, new { title, description = term });
            created.EnsureSuccessStatusCode();
        }

        string page = $"{Items}?q={Uri.EscapeDataString(term.ToUpperInvariant())}&pageSize=2";
        CatalogItemDescription[]? first = await app.Public.GetFromJsonAsync<CatalogItemDescription[]>(page);
        CatalogItemDescription[]? second = await app.Public.GetFromJsonAsync<CatalogItemDescription[]>(page + "&page=2");
        Assert.Equal(["Plane A", "Plane B"], first!.Select(item => item.Title));
        Assert.Equal("Plane C", Assert.Single(second!).Title);
        using HttpResponseMessage invalid = await app.Public.GetAsync($"{Items}?pageSize=0");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task CorrectingADescriptionPreservesTheCatalogIdentity()
    {
        using HttpResponseMessage created = await app.Staff.PostAsJsonAsync(Items, new { title = "Sander", description = "Old description." });
        CatalogItemDescription? original = await created.Content.ReadFromJsonAsync<CatalogItemDescription>();
        Assert.NotNull(original);
        using HttpResponseMessage updated = await app.Staff.PutAsJsonAsync(created.Headers.Location,
            new { title = "Orbital sander", description = "5-inch sanding pad." });
        updated.EnsureSuccessStatusCode();
        CatalogItemDescription? item = await app.Public.GetFromJsonAsync<CatalogItemDescription>(created.Headers.Location);
        Assert.Equal(new CatalogItemDescription(original.Id, "Orbital sander", "5-inch sanding pad."), item);
    }

    [Fact]
    public async Task ItemWritesRequireTheCatalogCapability()
    {
        using HttpResponseMessage anonymous = await app.Public.PostAsJsonAsync(Items, new { title = "Saw", description = "Pull saw." });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, Items)
        {
            Content = JsonContent.Create(new { title = "Saw", description = "Pull saw." })
        };
        request.Headers.Add("X-Test-Identity", "borrower");
        using HttpResponseMessage denied = await app.Public.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task InvalidInputAndUnknownIdReturnDocumentedErrors()
    {
        using HttpResponseMessage invalid = await app.Staff.PostAsJsonAsync(Items, new { title = "  ", description = "Saw." });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        JsonElement error = await invalid.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid-request", error.GetProperty("code").GetString());
        using HttpResponseMessage missing = await app.Public.GetAsync($"{Items}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        JsonElement missingError = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("catalog-item-not-found", missingError.GetProperty("code").GetString());
    }
}
