using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentLibrary.Lending.IntegrationEvents;
using Xunit;

namespace EquipmentLibrary.PickupDesk.Tests;

public sealed class PickupDeskTests(PickupFixture fixture) : IClassFixture<PickupFixture>, IAsyncLifetime
{
    public Task InitializeAsync() =>
        fixture.ExecuteAsync("TRUNCATE pickup_desk.holds, pickup_desk.inbox, pickup_desk.redrives");

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConsumerSpanRetainsTheFirstReceiptParentAcrossRedelivery()
    {
        HoldLifecycleEvent held = Place();
        ActivityContext producer;
        using (Activity original = new Activity("dispatch").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            producer = original.Context;
            await fixture.Inbox.AcceptAsync(held, default);
        }

        using (Activity duplicate = new Activity("redelivery").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            await fixture.Inbox.AcceptAsync(held, default);
        }

        Activity? observed = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "EquipmentLibrary.PickupDesk.Inbox",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed = activity
        };
        ActivitySource.AddActivityListener(listener);
        Assert.True(await fixture.Worker.ProcessNextAsync(default));
        Assert.NotNull(observed);
        Assert.Equal(ActivityKind.Consumer, observed.Kind);
        Assert.Equal(producer.TraceId, observed.TraceId);
        Assert.Equal(producer.SpanId, observed.ParentSpanId);
        Assert.Equal(held.Data.HoldId.ToString(), observed.GetTagItem("library.hold.id"));
        Assert.Equal(held.MessageId.ToString(), observed.GetTagItem("messaging.message.id"));
        Assert.Equal("completed", observed.GetTagItem("library.delivery.outcome"));
    }

    [Fact]
    public async Task RepeatedDeliveryAndConcurrentWorkersProduceOnePendingPickup()
    {
        HoldLifecycleEvent held = Place();
        await Task.WhenAll(fixture.Inbox.AcceptAsync(held, default), fixture.Inbox.AcceptAsync(held, default));
        bool[] outcomes = await Task.WhenAll(fixture.Worker.ProcessNextAsync(default), fixture.Worker.ProcessNextAsync(default));
        Assert.Single(outcomes, claimed => claimed);
        Assert.Equal(1, await fixture.CountAsync("SELECT count(*) FROM pickup_desk.inbox WHERE completed_at IS NOT NULL"));
        JsonElement queue = await ReadAsync($"/queue?copyId={held.Data.CopyId}");
        JsonElement entry = Assert.Single(queue.GetProperty("items").EnumerateArray());
        Assert.Equal(held.Data.HoldId, entry.GetProperty("holdId").GetGuid());
        Assert.False(entry.TryGetProperty("borrowerId", out _));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Inbox.AcceptAsync(held with { Data = held.Data with { Barcode = "different" } }, default));
    }

    [Theory]
    [InlineData("cancelled", HoldLifecycleEvent.Cancelled)]
    [InlineData("collected", HoldLifecycleEvent.Collected)]
    public async Task TerminalSnapshotRemainsClosedWhenPlacementArrivesLate(string state, string eventType)
    {
        HoldLifecycleEvent placed = Place();
        HoldLifecycleEvent closed = placed with
        {
            MessageId = Guid.NewGuid(), Sequence = 2, EventType = eventType, Data = placed.Data with { State = state }
        };
        await DeliverAsync(closed);
        await DeliverAsync(placed);
        Assert.Empty((await ReadAsync("/queue")).GetProperty("items").EnumerateArray());
        JsonElement history = await ReadAsync($"/history?copyId={placed.Data.CopyId}");
        Assert.Equal(state, Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.Conflict, await WriteAsync($"/queue/{placed.Data.HoldId}/claim"));
    }

    [Fact]
    public async Task TwoStaffCannotClaimTheSamePickup()
    {
        HoldLifecycleEvent placed = Place();
        await DeliverAsync(placed);
        string hold = $"/queue/{placed.Data.HoldId}";
        HttpStatusCode[] results = await Task.WhenAll(WriteAsync(hold + "/claim"), WriteAsync(hold + "/claim", "other"));
        Assert.Contains(HttpStatusCode.NoContent, results);
        Assert.Contains(HttpStatusCode.Conflict, results);
        Assert.Equal(1, (await ReadAsync("/workload")).GetProperty("inPreparation").GetInt64());
    }

    [Fact]
    public async Task StaffCanHandOffPreparationAndResolveAProblemBeforeMarkingReady()
    {
        HoldLifecycleEvent placed = Place();
        await DeliverAsync(placed);
        string hold = $"/queue/{placed.Data.HoldId}";
        Assert.Equal(HttpStatusCode.NoContent, await WriteAsync(hold + "/claim"));
        Assert.Equal(HttpStatusCode.Conflict, await WriteAsync(hold + "/release", "other"));
        Assert.Equal(HttpStatusCode.NoContent, await WriteAsync(hold + "/release"));
        Assert.Equal(HttpStatusCode.NoContent, await WriteAsync(hold + "/claim", "other"));
        Assert.Equal(HttpStatusCode.BadRequest, await WriteAsync(hold + "/issues", "other", new { reason = " " }));
        Assert.Equal(HttpStatusCode.NoContent, await WriteAsync(hold + "/issues", "other", new { reason = "Replace battery" }));
        Assert.Equal(1, (await ReadAsync("/workload")).GetProperty("blocked").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, await WriteAsync(hold + "/ready", "other"));
        Assert.Equal(HttpStatusCode.NoContent, await WriteAsync(hold + "/issues/resolve", "other"));
        Assert.Equal(HttpStatusCode.NoContent, await WriteAsync(hold + "/ready", "other"));
        JsonElement report = await ReadAsync("/workload");
        Assert.Equal(1, report.GetProperty("ready").GetInt64());
        Assert.NotNull((await ReadAsync(hold)).GetProperty("readyAt").GetString());
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("borrower", HttpStatusCode.Forbidden)]
    public async Task QueueRequiresStaffPermission(string? actor, HttpStatusCode expected)
    {
        using HttpResponseMessage queue = await SendAsync(HttpMethod.Get, "/queue", actor);
        Assert.Equal(expected, queue.StatusCode);
        Assert.Equal(expected, await WriteAsync($"/queue/{Guid.NewGuid()}/claim", actor));
    }

    [Fact]
    public async Task BadReceiptIsQuarantinedWithoutStoppingOtherReceipts()
    {
        var invalid = Guid.NewGuid();
        await fixture.ExecuteAsync($$"""
            INSERT INTO pickup_desk.inbox (subscriber_id, message_id, payload)
            VALUES ('pickup-desk.queue.v1', '{{invalid}}', '{}')
            """);
        Assert.True(await fixture.Worker.ProcessNextAsync(default));
        await DeliverAsync(Place());
        using HttpResponseMessage denied = await SendAsync(HttpMethod.Get, "/inbox/quarantine", "staff");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        JsonElement quarantine = await ReadAsync("/inbox/quarantine", "operator");
        Assert.Equal(invalid, Assert.Single(quarantine.GetProperty("items").EnumerateArray()).GetProperty("messageId").GetGuid());
        Assert.Equal(HttpStatusCode.Forbidden, await WriteAsync($"/inbox/{invalid}/redrive", "staff", new { reason = "Reviewed" }));
        Assert.Equal(HttpStatusCode.NoContent, await WriteAsync($"/inbox/{invalid}/redrive", "operator", new { reason = "Decoder reviewed" }));
        Assert.Equal(HttpStatusCode.Conflict, await WriteAsync($"/inbox/{invalid}/redrive", "operator", new { reason = "Repeat" }));
        Assert.Equal(1, await fixture.CountAsync("SELECT count(*) FROM pickup_desk.redrives"));
        Assert.True(await fixture.Worker.ProcessNextAsync(default));
        Assert.Equal(1, await fixture.CountAsync("SELECT count(*) FROM pickup_desk.inbox WHERE quarantined_at IS NOT NULL"));
    }

    [Fact]
    public async Task FailedCompletionRollsBackProjectionAndOperatorCanRecoverTheOriginalReceipt()
    {
        HoldLifecycleEvent held = Place();
        await fixture.Inbox.AcceptAsync(held, default);
        await fixture.ExecuteAsync("""
            CREATE FUNCTION pickup_desk.fail_completion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.completed_at IS NOT NULL THEN RAISE EXCEPTION 'simulated completion failure'; END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER fail_completion BEFORE UPDATE ON pickup_desk.inbox
            FOR EACH ROW EXECUTE FUNCTION pickup_desk.fail_completion()
            """);
        try
        {
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                Assert.True(await fixture.Worker.ProcessNextAsync(default));
                Assert.Equal(0, await fixture.CountAsync("SELECT count(*) FROM pickup_desk.holds"));
                Assert.False(await fixture.Worker.ProcessNextAsync(default));
                await fixture.ExecuteAsync("UPDATE pickup_desk.inbox SET available_at = now() WHERE completed_at IS NULL");
            }
        }
        finally
        {
            await fixture.ExecuteAsync("DROP TRIGGER fail_completion ON pickup_desk.inbox; DROP FUNCTION pickup_desk.fail_completion()");
        }

        Assert.Equal(1, await fixture.CountAsync("SELECT count(*) FROM pickup_desk.inbox WHERE attempts = 5 AND quarantined_at IS NOT NULL"));
        Assert.Equal(HttpStatusCode.NoContent, await WriteAsync($"/inbox/{held.MessageId}/redrive", "operator", new { reason = "Fault removed" }));
        Assert.True(await fixture.Worker.ProcessNextAsync(default));
        await fixture.Inbox.AcceptAsync(held, default);
        Assert.False(await fixture.Worker.ProcessNextAsync(default));
        Assert.Equal(1, await fixture.CountAsync("SELECT count(*) FROM pickup_desk.holds WHERE hold_state = 'held'"));
        Assert.Equal(1, await fixture.CountAsync("SELECT count(*) FROM pickup_desk.inbox WHERE completed_at IS NOT NULL"));
    }

    private static HoldLifecycleEvent Place() =>
        new(Guid.NewGuid(), HoldLifecycleEvent.Placed, 1, 1, DateTimeOffset.UtcNow,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "DRILL-08", "held", "Cordless drill"));

    private async Task DeliverAsync(HoldLifecycleEvent message)
    {
        await fixture.Inbox.AcceptAsync(message, default);
        Assert.True(await fixture.Worker.ProcessNextAsync(default));
    }

    private async Task<JsonElement> ReadAsync(string route, string actor = "staff")
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, route, actor);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpStatusCode> WriteAsync(string route, string? actor = "staff", object? body = null)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, route, actor, body);
        return response.StatusCode;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string route, string? actor, object? body = null)
    {
        using var request = new HttpRequestMessage(method, "/api/pickup-desk" + route);
        if (actor is not null)
        {
            request.Headers.Add("X-Test-Actor", actor);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await fixture.Client.SendAsync(request);
    }
}
