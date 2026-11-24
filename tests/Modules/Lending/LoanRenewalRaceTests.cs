using System.Net;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

[Collection(nameof(LendingScenarios))]
public sealed class LoanRenewalRaceTests(LendingFixture fixture) : IDisposable
{
    public void Dispose() => fixture.Clock.Reset();

    [Fact]
    public async Task ARequestWaitingOnTheLoanLockCannotRenewAtTheDeadline()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient borrower = fixture.Client("a");
        var checkout = new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
        fixture.Clock.Set(checkout);
        Guid loan = await LoanLifecycleTests.CollectAsync(staff, borrower, await ReservationTests.EnrollAsync(staff));
        using HttpResponseMessage before = await borrower.GetAsync($"/api/lending/loans/{loan}");
        DateTimeOffset due = (await ReservationTests.Json(before)).GetProperty("dueAt").GetDateTimeOffset();
        fixture.Clock.Set(due.AddSeconds(-1));

        await using NpgsqlConnection connection = await fixture.Admin.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using var locked = new NpgsqlCommand("SELECT id FROM lending.loans WHERE id = $1 FOR UPDATE", connection);
        locked.Parameters.AddWithValue(loan);
        Assert.Equal(loan, await locked.ExecuteScalarAsync());

        Task<HttpResponseMessage> pending = borrower.PostAsync($"/api/lending/loans/{loan}/renew", null);
        await using NpgsqlCommand waiting = fixture.Admin.CreateCommand("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND $1 = ANY(pg_blocking_pids(pid)))
            """);
        waiting.Parameters.AddWithValue(connection.ProcessID);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await waiting.ExecuteScalarAsync(timeout.Token) is not true)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        fixture.Clock.Set(due);
        await transaction.CommitAsync();
        using HttpResponseMessage result = await pending;
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("loan-not-renewable", (await ReservationTests.Json(result)).GetProperty("code").GetString());
        using HttpResponseMessage after = await borrower.GetAsync($"/api/lending/loans/{loan}");
        JsonElement persisted = await ReservationTests.Json(after);
        Assert.Equal(due, persisted.GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal(0, persisted.GetProperty("renewals").GetInt32());
    }

    [Fact]
    public async Task ConcurrentRenewalsExtendTheDueDateExactlyOnce()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient borrower = fixture.Client("a");
        Guid loan = await LoanLifecycleTests.CollectAsync(staff, borrower, await ReservationTests.EnrollAsync(staff));
        using HttpResponseMessage before = await borrower.GetAsync($"/api/lending/loans/{loan}");
        DateTimeOffset due = (await ReservationTests.Json(before)).GetProperty("dueAt").GetDateTimeOffset();
        string url = $"/api/lending/loans/{loan}/renew";
        HttpResponseMessage[] responses = await Task.WhenAll(borrower.PostAsync(url, null), borrower.PostAsync(url, null));

        using HttpResponseMessage winner = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        using HttpResponseMessage loser = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(due.AddDays(14), (await ReservationTests.Json(winner)).GetProperty("dueAt").GetDateTimeOffset());
        Assert.Equal("loan-not-renewable", (await ReservationTests.Json(loser)).GetProperty("code").GetString());
        using HttpResponseMessage after = await borrower.GetAsync($"/api/lending/loans/{loan}");
        Assert.Equal(1, (await ReservationTests.Json(after)).GetProperty("renewals").GetInt32());
    }
}
