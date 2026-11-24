using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

[Collection(nameof(LendingScenarios))]
public sealed class ReservationTests(LendingFixture fixture)
{
    [Fact]
    public async Task LastCopyHasOneWinnerAndOneOutboxEvent()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient first = fixture.Client("a");
        using HttpClient second = fixture.Client("b");
        Guid copyId = await EnrollAsync(staff);
        HttpResponseMessage[] responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/lending/holds", new { copyId }), second.PostAsJsonAsync("/api/lending/holds", new { copyId }));
        using HttpResponseMessage winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        using HttpResponseMessage loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("copy-unavailable", (await Json(loser)).GetProperty("code").GetString());
        Guid holdId = (await Json(winner)).GetProperty("holdId").GetGuid();
        Assert.Equal($"/api/lending/holds/{holdId}", winner.Headers.Location?.OriginalString);
        Assert.Equal(1L, await CountEvents(holdId));

        using HttpResponseMessage staffRead = await staff.GetAsync($"/api/lending/holds/{holdId}");
        Assert.Equal(HttpStatusCode.OK, staffRead.StatusCode);
        HttpClient other = responses[0] == winner ? second : first;
        using HttpResponseMessage denied = await other.PostAsync($"/api/lending/holds/{holdId}/cancel", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using HttpResponseMessage foreignRead = await other.GetAsync($"/api/lending/holds/{holdId}");
        Assert.Equal(HttpStatusCode.Forbidden, foreignRead.StatusCode);
    }

    [Fact]
    public async Task CancellingAndCollectingConcurrentlyCommitOneTerminalTransition()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        Guid copyId = await EnrollAsync(staff);
        using HttpResponseMessage placed = await member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Guid holdId = (await Json(placed)).GetProperty("holdId").GetGuid();
        HttpResponseMessage[] responses = await Task.WhenAll(
            staff.PostAsync($"/api/lending/holds/{holdId}/collect", null), member.PostAsync($"/api/lending/holds/{holdId}/cancel", null));
        using HttpResponseMessage winner = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        using HttpResponseMessage loser = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("hold-not-active", (await Json(loser)).GetProperty("code").GetString());
        Assert.Equal(2L, await CountEvents(holdId));
        string expected = (await Json(winner)).GetProperty("state").GetString() == "collected" ? "on-loan" : "available";
        using HttpResponseMessage copy = await staff.GetAsync($"/api/lending/copies/{copyId}");
        Assert.Equal(expected, (await Json(copy)).GetProperty("state").GetString());
    }

    private async Task<long> CountEvents(Guid holdId)
    {
        await using NpgsqlCommand query = fixture.Admin.CreateCommand(
            "SELECT count(*) FROM lending.outbox WHERE (payload -> 'data' ->> 'holdId')::uuid = $1");
        query.Parameters.Add(new NpgsqlParameter { Value = holdId });
        return (long)(await query.ExecuteScalarAsync())!;
    }

    internal static async Task<Guid> EnrollAsync(HttpClient staff)
    {
        using HttpResponseMessage response = await staff.PostAsJsonAsync("/api/lending/copies",
            new { catalogItemId = LendingFixture.CatalogItemId, barcode = Guid.NewGuid().ToString("N") });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Json(response)).GetProperty("copyId").GetGuid();
    }

    internal static Task<JsonElement> Json(HttpResponseMessage response) => response.Content.ReadFromJsonAsync<JsonElement>();
}
