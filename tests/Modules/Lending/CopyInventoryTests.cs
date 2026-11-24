using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

[Collection(nameof(LendingScenarios))]
public sealed class CopyInventoryTests(LendingFixture fixture)
{
    [Fact]
    public async Task BarcodeEnrollmentReusesTheCopyAndRejectsAnUnknownItem()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        string barcode = Guid.NewGuid().ToString("N");
        var request = new { catalogItemId = LendingFixture.CatalogItemId, barcode };
        using HttpResponseMessage denied = await member.PostAsJsonAsync("/api/lending/copies", request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using HttpResponseMessage created = await staff.PostAsJsonAsync("/api/lending/copies", request);
        using HttpResponseMessage replayed = await staff.PostAsJsonAsync("/api/lending/copies", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayed.StatusCode);
        Assert.Equal((await ReservationTests.Json(created)).GetProperty("copyId").GetGuid(),
            (await ReservationTests.Json(replayed)).GetProperty("copyId").GetGuid());
        using HttpResponseMessage missing = await staff.PostAsJsonAsync("/api/lending/copies",
            new { catalogItemId = Guid.NewGuid(), barcode = "UNKNOWN-" + barcode });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using HttpResponseMessage bad = await staff.PostAsJsonAsync("/api/lending/copies",
            new { catalogItemId = LendingFixture.CatalogItemId, barcode = " " });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task RetirementPreventsReservationsAndReinstatementRestoresAvailability()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        Guid copyId = await ReservationTests.EnrollAsync(staff);
        string url = $"/api/lending/copies/{copyId}";
        using HttpResponseMessage retired = await staff.PostAsync($"{url}/retire", null);
        Assert.Equal("retired", (await ReservationTests.Json(retired)).GetProperty("state").GetString());
        using HttpResponseMessage available = await member.GetAsync($"/api/lending/availability/{LendingFixture.CatalogItemId}");
        Assert.True((await ReservationTests.Json(available)).GetProperty("retired").GetInt32() >= 1);
        using HttpResponseMessage retiredList = await member.GetAsync($"/api/lending/copies?state=retired&catalogItemId={LendingFixture.CatalogItemId}");
        Assert.Contains((await ReservationTests.Json(retiredList)).GetProperty("items").EnumerateArray(),
            item => item.GetProperty("copyId").GetGuid() == copyId);
        using HttpResponseMessage unavailable = await member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        using HttpResponseMessage reinstated = await staff.PostAsync($"{url}/reinstate", null);
        Assert.Equal("available", (await ReservationTests.Json(reinstated)).GetProperty("state").GetString());
        using HttpResponseMessage reserved = await member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Guid holdId = (await ReservationTests.Json(reserved)).GetProperty("holdId").GetGuid();
        using HttpResponseMessage mine = await member.GetAsync("/api/lending/me/holds");
        Assert.Contains((await ReservationTests.Json(mine)).GetProperty("items").EnumerateArray(), h => h.GetProperty("holdId").GetGuid() == holdId);
        using HttpResponseMessage busy = await staff.PostAsync($"{url}/retire", null);
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
        using HttpResponseMessage cancelled = await member.PostAsync($"/api/lending/holds/{holdId}/cancel", null);
        Assert.Equal("cancelled", (await ReservationTests.Json(cancelled)).GetProperty("state").GetString());
        using HttpResponseMessage empty = await member.GetAsync("/api/lending/copies?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }
}
