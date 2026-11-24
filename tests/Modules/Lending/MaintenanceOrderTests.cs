using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

[Collection(nameof(LendingScenarios))]
public sealed class MaintenanceOrderTests(LendingFixture fixture)
{
    [Fact]
    public async Task RepairRestoresCirculationAndAnOldRetryCannotReleaseTheNextHold()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        Guid copyId = await ReservationTests.EnrollAsync(staff);
        string intake = $"/api/lending/copies/{copyId}/maintenance";
        using HttpResponseMessage denied = await member.PostAsJsonAsync(intake, new { reason = "Frayed cable" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using HttpResponseMessage empty = await staff.PostAsJsonAsync(intake, new { reason = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        using HttpResponseMessage created = await staff.PostAsJsonAsync(intake, new { reason = " Frayed cable " });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Uri location = created.Headers.Location!;
        JsonElement order = await ReservationTests.Json(created);
        Assert.Equal("Frayed cable", order.GetProperty("reason").GetString());
        using HttpResponseMessage queue = await staff.GetAsync("/api/lending/maintenance?limit=100");
        Assert.Contains((await ReservationTests.Json(queue)).GetProperty("items").EnumerateArray(),
            row => row.GetProperty("id").GetGuid() == order.GetProperty("id").GetGuid());
        using HttpResponseMessage unavailable = await member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Conflict, unavailable.StatusCode);
        using HttpResponseMessage repaired = await staff.PostAsJsonAsync($"{location}/repair", new { note = "Replaced cable" });
        string? closedAt = (await ReservationTests.Json(repaired)).GetProperty("closedAt").GetString();
        using HttpResponseMessage reserved = await member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Created, reserved.StatusCode);
        using HttpResponseMessage retry = await staff.PostAsJsonAsync($"{location}/repair", new { note = "Replaced cable" });
        Assert.Equal(closedAt, (await ReservationTests.Json(retry)).GetProperty("closedAt").GetString());
        using HttpResponseMessage reverse = await staff.PostAsJsonAsync($"{location}/withdraw", new { note = "Retire" });
        Assert.Equal(HttpStatusCode.Conflict, reverse.StatusCode);
        using HttpResponseMessage copy = await staff.GetAsync($"/api/lending/copies/{copyId}");
        Assert.Equal("held", (await ReservationTests.Json(copy)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task FailedInspectionCanWithdrawACopyAndConcurrentIntakeOnlyOpensOneOrder()
    {
        using HttpClient staff = fixture.Client("staff");
        Guid copyId = await ReservationTests.EnrollAsync(staff);
        string intake = $"/api/lending/copies/{copyId}/maintenance";
        var note = new { reason = "Broken chuck" };
        HttpResponseMessage[] attempts = await Task.WhenAll(staff.PostAsJsonAsync(intake, note), staff.PostAsJsonAsync(intake, note));
        using HttpResponseMessage opened = Assert.Single(attempts, result => result.StatusCode == HttpStatusCode.Created);
        using HttpResponseMessage duplicate = Assert.Single(attempts, result => result.StatusCode == HttpStatusCode.Conflict);
        using HttpResponseMessage withdrawn = await staff.PostAsJsonAsync($"{opened.Headers.Location}/withdraw", new { note = "Beyond repair" });
        Assert.Equal("withdrawn", (await ReservationTests.Json(withdrawn)).GetProperty("outcome").GetString());
        using HttpResponseMessage order = await staff.GetAsync(opened.Headers.Location);
        Assert.Equal("Beyond repair", (await ReservationTests.Json(order)).GetProperty("resolution").GetString());
        using HttpResponseMessage copy = await staff.GetAsync($"/api/lending/copies/{copyId}");
        Assert.Equal("retired", (await ReservationTests.Json(copy)).GetProperty("state").GetString());
    }

    [Fact]
    public async Task StaffInspectionAndBorrowerReservationCompeteForTheSameCopy()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        Guid copyId = await ReservationTests.EnrollAsync(staff);
        Task<HttpResponseMessage> inspection = staff.PostAsJsonAsync($"/api/lending/copies/{copyId}/maintenance", new { reason = "Check charger" });
        Task<HttpResponseMessage> hold = member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        await Task.WhenAll(inspection, hold);
        using HttpResponseMessage serviceResult = await inspection;
        using HttpResponseMessage holdResult = await hold;
        Assert.Equal(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, new[] { serviceResult.StatusCode, holdResult.StatusCode }.Order());
        using HttpResponseMessage copy = await staff.GetAsync($"/api/lending/copies/{copyId}");
        Assert.Equal(serviceResult.StatusCode == HttpStatusCode.Created ? "maintenance" : "held",
            (await ReservationTests.Json(copy)).GetProperty("state").GetString());
    }
}
