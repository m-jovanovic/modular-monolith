using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

[Collection(nameof(LendingScenarios))]
public sealed class LoanLifecycleTests(LendingFixture fixture)
{
    [Fact]
    public async Task BorrowerRenewsOnceAndReturnRetriesCannotReleaseTheNextHold()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient alice = fixture.Client("a");
        using HttpClient bob = fixture.Client("b");
        Guid copyId = await ReservationTests.EnrollAsync(staff);
        Guid loanId = await CollectAsync(staff, alice, copyId);
        string loanUrl = $"/api/lending/loans/{loanId}";

        using HttpResponseMessage original = await alice.GetAsync(loanUrl);
        DateTimeOffset due = (await ReservationTests.Json(original)).GetProperty("dueAt").GetDateTimeOffset();
        using HttpResponseMessage deniedRead = await bob.GetAsync(loanUrl);
        using HttpResponseMessage deniedRenew = await bob.PostAsync($"{loanUrl}/renew", null);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRenew.StatusCode);

        using HttpResponseMessage renewed = await alice.PostAsync($"{loanUrl}/renew", null);
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
        Assert.Equal(due.AddDays(14), (await ReservationTests.Json(renewed)).GetProperty("dueAt").GetDateTimeOffset());
        using HttpResponseMessage secondRenewal = await alice.PostAsync($"{loanUrl}/renew", null);
        Assert.Equal(HttpStatusCode.Conflict, secondRenewal.StatusCode);
        using HttpResponseMessage mine = await alice.GetAsync("/api/lending/me/loans?openOnly=true");
        Assert.Contains((await ReservationTests.Json(mine)).GetProperty("items").EnumerateArray(), l => l.GetProperty("loanId").GetGuid() == loanId);

        using HttpResponseMessage returned = await staff.PostAsync($"{loanUrl}/return", null);
        Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
        string? returnedAt = (await ReservationTests.Json(returned)).GetProperty("returnedAt").GetString();
        Assert.NotNull(returnedAt);
        using HttpResponseMessage placed = await bob.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        using HttpResponseMessage retried = await staff.PostAsync($"{loanUrl}/return", null);
        Assert.Equal(returnedAt, (await ReservationTests.Json(retried)).GetProperty("returnedAt").GetString());
        using HttpResponseMessage copy = await alice.GetAsync($"/api/lending/copies/{copyId}");
        Assert.Equal("held", (await ReservationTests.Json(copy)).GetProperty("state").GetString());
        using HttpResponseMessage history = await staff.GetAsync($"/api/lending/copies/{copyId}/history");
        JsonElement entries = (await ReservationTests.Json(history)).GetProperty("items");
        Assert.Equal(2, entries.GetArrayLength());
        Assert.Contains(entries.EnumerateArray(), h => h.GetProperty("holdId").GetGuid() == loanId &&
            h.GetProperty("returnedAt").ValueKind == JsonValueKind.String);
    }

    [Fact]
    public async Task StaffCanFindAnOverdueLoanAndBorrowerCannotRenewIt()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        Guid copyId = await ReservationTests.EnrollAsync(staff);
        Guid loanId = await CollectAsync(staff, member, copyId);
        await using NpgsqlCommand age = fixture.Admin.CreateCommand("""
            UPDATE lending.loans SET checked_out_at = now() - interval '15 days', due_at = now() - interval '1 day' WHERE id = $1
            """);
        age.Parameters.Add(new NpgsqlParameter { Value = loanId });
        await age.ExecuteNonQueryAsync();

        using HttpResponseMessage denied = await member.GetAsync("/api/lending/loans/overdue");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using HttpResponseMessage overdue = await staff.GetAsync("/api/lending/loans/overdue");
        Assert.Contains((await ReservationTests.Json(overdue)).GetProperty("items").EnumerateArray(), l => l.GetProperty("loanId").GetGuid() == loanId);
        using HttpResponseMessage renewal = await member.PostAsync($"/api/lending/loans/{loanId}/renew", null);
        Assert.Equal(HttpStatusCode.Conflict, renewal.StatusCode);
    }

    internal static async Task<Guid> CollectAsync(HttpClient staff, HttpClient borrower, Guid copyId)
    {
        using HttpResponseMessage held = await borrower.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Created, held.StatusCode);
        Guid holdId = (await ReservationTests.Json(held)).GetProperty("holdId").GetGuid();
        using HttpResponseMessage collected = await staff.PostAsync($"/api/lending/holds/{holdId}/collect", null);
        Assert.Equal(HttpStatusCode.OK, collected.StatusCode);
        return holdId;
    }
}
