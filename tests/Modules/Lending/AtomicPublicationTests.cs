using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

[Collection(nameof(LendingScenarios))]
public sealed class AtomicPublicationTests(LendingFixture fixture)
{
    [Fact]
    public async Task FailedOutboxInsertRollsBackBothHoldAndCopyClaim()
    {
        using HttpClient staff = fixture.Client("staff");
        using HttpClient member = fixture.Client("a");
        Guid copyId = await ReservationTests.EnrollAsync(staff);
        // This single-copy constraint lets us fail the final write of the real transaction.
        await using NpgsqlCommand deny = fixture.Admin.CreateCommand($"""
            ALTER TABLE lending.outbox ADD CONSTRAINT fixture_refuse_event
            CHECK ((payload -> 'data' ->> 'copyId')::uuid <> '{copyId:D}'::uuid) NOT VALID
            """);
        await deny.ExecuteNonQueryAsync();
        try
        {
            PostgresException failure = await Assert.ThrowsAsync<PostgresException>(
                () => member.PostAsJsonAsync("/api/lending/holds", new { copyId }));
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            using HttpResponseMessage copy = await staff.GetAsync($"/api/lending/copies/{copyId}");
            Assert.Equal("available", (await ReservationTests.Json(copy)).GetProperty("state").GetString());
            await using NpgsqlCommand count = fixture.Admin.CreateCommand("SELECT count(*) FROM lending.holds WHERE copy_id = $1");
            count.Parameters.Add(new NpgsqlParameter { Value = copyId });
            Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
        }
        finally
        {
            await using NpgsqlCommand allow = fixture.Admin.CreateCommand("ALTER TABLE lending.outbox DROP CONSTRAINT fixture_refuse_event");
            await allow.ExecuteNonQueryAsync();
        }

        using HttpResponseMessage retried = await member.PostAsJsonAsync("/api/lending/holds", new { copyId });
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
    }
}
