using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EquipmentLibrary.Lending.IntegrationEvents;
using Npgsql;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

[Collection(nameof(LendingScenarios))]
public sealed class HoldExpiryTests(LendingFixture fixture) : IDisposable
{
    public void Dispose() => fixture.Clock.Reset();

    [Fact]
    public async Task CompetingExpiryAndCollectionReleaseOneCopyAndPublishOneCompatibleCancellation()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        fixture.Clock.Set(new DateTimeOffset(2019, 4, 5, 9, 0, 0, TimeSpan.Zero));
        (Guid holdId, Guid copyId, DateTimeOffset deadline) = await PlaceAsync(staff, member);
        using HttpResponseMessage denied = await member.PostAsync("/api/lending/holds/expire-due", null);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using HttpResponseMessage invalid = await staff.PostAsync("/api/lending/holds/expire-due?limit=101", null);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        fixture.Clock.Set(deadline.AddTicks(-10));
        Assert.Equal(0, await SweepAsync(staff));
        fixture.Clock.Set(deadline);
        using HttpResponseMessage lateCollect = await staff.PostAsync($"/api/lending/holds/{holdId}/collect", null);
        Assert.Equal("pickup-window-closed", (await ReservationTests.Json(lateCollect)).GetProperty("code").GetString());

        Task<HttpResponseMessage> competingCollection = staff.PostAsync($"/api/lending/holds/{holdId}/collect", null);
        int[] counts = await Task.WhenAll(SweepAsync(staff), SweepAsync(staff));
        using HttpResponseMessage collection = await competingCollection;
        Assert.Equal(HttpStatusCode.Conflict, collection.StatusCode);
        // Both SKIP LOCKED sweeps can run while the collector holds the row.
        int expiredAfterCollection = await SweepAsync(staff);
        Assert.Equal(1, counts.Sum() + expiredAfterCollection);
        Assert.Equal(0, await SweepAsync(staff));

        using HttpResponseMessage hold = await member.GetAsync($"/api/lending/holds/{holdId}");
        JsonElement closed = await ReservationTests.Json(hold);
        Assert.Equal("cancelled", closed.GetProperty("state").GetString());
        Assert.True(closed.GetProperty("expired").GetBoolean());
        await using NpgsqlCommand verify = fixture.Admin.CreateCommand("""
            SELECT payload::text FROM lending.outbox WHERE payload->'data'->>'holdId' = $1
            ORDER BY (payload->>'sequence')::int
            """);
        verify.Parameters.Add(new NpgsqlParameter { Value = holdId.ToString() });
        var messages = new List<HoldLifecycleEvent>();
        await using (NpgsqlDataReader reader = await verify.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                messages.Add(HoldEventWire.Read(Encoding.UTF8.GetBytes(reader.GetString(0))));
            }
        }

        Assert.Collection(messages,
            placed => Assert.Equal(HoldLifecycleEvent.Placed, placed.EventType),
            terminal =>
            {
                Assert.Equal(HoldLifecycleEvent.Cancelled, terminal.EventType);
                Assert.Equal(1, terminal.SchemaVersion);
                Assert.Equal(2, terminal.Sequence);
                Assert.Equal(deadline, terminal.OccurredAt);
                Assert.Equal("cancelled", terminal.Data.State);
            });
        using HttpResponseMessage loan = await member.GetAsync($"/api/lending/loans/{holdId}");
        Assert.Equal(HttpStatusCode.NotFound, loan.StatusCode);
        using HttpResponseMessage reheld = await member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Created, reheld.StatusCode);
        Guid nextId = (await ReservationTests.Json(reheld)).GetProperty("holdId").GetGuid();
        using HttpResponseMessage cancelNext = await member.PostAsync($"/api/lending/holds/{nextId}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelNext.StatusCode);
    }

    [Fact]
    public async Task CollectionUsesTheClockAfterTheRowLockBecomesAvailable()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        fixture.Clock.Set(DateTimeOffset.UnixEpoch);
        (Guid holdId, _, DateTimeOffset deadline) = await PlaceAsync(staff, member);
        fixture.Clock.Set(deadline.AddSeconds(-1));

        await using NpgsqlConnection blocker = await fixture.Admin.OpenConnectionAsync();
        await using NpgsqlTransaction transaction = await blocker.BeginTransactionAsync();
        await using var lockHold = new NpgsqlCommand("SELECT id FROM lending.holds WHERE id = $1 FOR UPDATE", blocker, transaction);
        lockHold.Parameters.AddWithValue(holdId);
        Assert.Equal(holdId, (Guid)(await lockHold.ExecuteScalarAsync())!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<HttpResponseMessage> collecting = staff.PostAsync($"/api/lending/holds/{holdId}/collect", null, timeout.Token);
        await using NpgsqlCommand waiting = fixture.Admin.CreateCommand("""
            SELECT EXISTS (SELECT 1 FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock' AND $1 = ANY(pg_blocking_pids(pid)))
            """);
        waiting.Parameters.AddWithValue(blocker.ProcessID);
        while (!(bool)(await waiting.ExecuteScalarAsync(timeout.Token))!)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(15), timeout.Token);
        }

        fixture.Clock.Set(deadline);
        await transaction.RollbackAsync(timeout.Token);
        using HttpResponseMessage result = await collecting;
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("pickup-window-closed", (await ReservationTests.Json(result)).GetProperty("code").GetString());
        using HttpResponseMessage loan = await member.GetAsync($"/api/lending/loans/{holdId}");
        Assert.Equal(HttpStatusCode.NotFound, loan.StatusCode);
    }

    [Fact]
    public async Task HostedWorkerReleasesAnExpiredHoldWhenItsClockTicks()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        fixture.Clock.Set(new DateTimeOffset(2017, 9, 9, 12, 0, 0, TimeSpan.Zero));
        (Guid holdId, _, DateTimeOffset deadline) = await PlaceAsync(staff, member);
        fixture.Clock.Set(deadline);
        await fixture.Clock.TickWorkerAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            using HttpResponseMessage response = await member.GetAsync($"/api/lending/holds/{holdId}", timeout.Token);
            if ((await ReservationTests.Json(response)).GetProperty("expired").GetBoolean())
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    [Fact]
    public async Task SweepSkipsLockedRowsAndRespectsTheRequestedBatchLimit()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("b");
        var start = new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero);
        fixture.Clock.Set(start);
        (Guid HoldId, Guid CopyId, DateTimeOffset Deadline) oldest = await PlaceAsync(staff, member);
        fixture.Clock.Set(start.AddMinutes(1));
        (Guid HoldId, Guid CopyId, DateTimeOffset Deadline) second = await PlaceAsync(staff, member);
        fixture.Clock.Set(start.AddMinutes(2));
        (Guid HoldId, Guid CopyId, DateTimeOffset Deadline) third = await PlaceAsync(staff, member);
        fixture.Clock.Set(third.Deadline);
        await using NpgsqlConnection connection = await fixture.Admin.OpenConnectionAsync();
        await using NpgsqlTransaction heldLock = await connection.BeginTransactionAsync();
        await using NpgsqlCommand busy = new("SELECT id FROM lending.holds WHERE id = $1 FOR UPDATE", connection);
        busy.Parameters.Add(new NpgsqlParameter { Value = oldest.HoldId });
        await busy.ExecuteScalarAsync();

        Assert.Equal(1, await SweepAsync(staff));
        using HttpResponseMessage next = await member.GetAsync($"/api/lending/holds/{second.HoldId}");
        Assert.True((await ReservationTests.Json(next)).GetProperty("expired").GetBoolean());
        using HttpResponseMessage last = await member.GetAsync($"/api/lending/holds/{third.HoldId}");
        Assert.Equal("held", (await ReservationTests.Json(last)).GetProperty("state").GetString());
        await heldLock.RollbackAsync();
        Assert.Equal(1, await SweepAsync(staff));
        Assert.Equal(1, await SweepAsync(staff));
        Assert.Equal(0, await SweepAsync(staff));
    }

    private static async Task<int> SweepAsync(HttpClient staff)
    {
        using HttpResponseMessage response = await staff.PostAsync("/api/lending/holds/expire-due?limit=1", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReservationTests.Json(response)).GetProperty("expiredCount").GetInt32();
    }

    private static async Task<(Guid HoldId, Guid CopyId, DateTimeOffset Deadline)> PlaceAsync(HttpClient staff, HttpClient member)
    {
        Guid copyId = await ReservationTests.EnrollAsync(staff);
        using HttpResponseMessage response = await member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement placed = await ReservationTests.Json(response);
        return (placed.GetProperty("holdId").GetGuid(), copyId, placed.GetProperty("pickupDeadline").GetDateTimeOffset());
    }
}
