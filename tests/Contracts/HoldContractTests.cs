using System.Text.Json;
using System.Text.Json.Nodes;
using EquipmentLibrary.Catalog.Contracts;
using EquipmentLibrary.Lending.IntegrationEvents;
using Xunit;

namespace EquipmentLibrary.Contracts.Tests;

public sealed class HoldContractTests
{
    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixtures", name));

    [Theory]
    [InlineData("held.json", HoldLifecycleEvent.Placed, "held", 1)]
    [InlineData("collected.json", HoldLifecycleEvent.Collected, "collected", 2)]
    [InlineData("cancelled.json", HoldLifecycleEvent.Cancelled, "cancelled", 2)]
    public void PublishedSnapshotsRoundTripWithStableWireNames(
        string fixture, string eventType, string state, int sequence)
    {
        byte[] input = Fixture(fixture);
        HoldLifecycleEvent message = HoldEventWire.Read(input);

        Assert.Equal(eventType, message.EventType);
        Assert.Equal(state, message.Data.State);
        Assert.Equal(sequence, message.Sequence);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(input), JsonNode.Parse(HoldEventWire.Write(message))));
    }

    [Fact]
    public void ConsumersReadEarlierAndAdditiveMessages()
    {
        HoldLifecycleEvent earlier = HoldEventWire.Read(Fixture("held-earlier.json"));
        HoldLifecycleEvent current = HoldEventWire.Read(Fixture("held.json"));
        Assert.Null(earlier.Data.ItemTitle);
        Assert.Equal(current with { Data = current.Data with { ItemTitle = null } }, earlier);
        Assert.Equal(HoldEventWire.Read(Fixture("second-held.json")), HoldEventWire.Read(Fixture("held-additive.json")));
    }

    [Fact]
    public void UnknownVersionsConflictingTransitionsAndMissingFieldsAreRejected()
    {
        HoldLifecycleEvent message = HoldEventWire.Read(Fixture("held.json"));

        Assert.Throws<JsonException>(() => HoldEventWire.Write(message with { SchemaVersion = 2 }));
        Assert.Throws<JsonException>(() => HoldEventWire.Write(message with { EventType = HoldLifecycleEvent.Cancelled }));
        Assert.Throws<JsonException>(() => HoldEventWire.Write(message with { Sequence = 0 }));

        JsonObject document = JsonNode.Parse(Fixture("held.json"))!.AsObject();
        document.Remove("messageId");
        Assert.Throws<JsonException>(() => HoldEventWire.Read(JsonSerializer.SerializeToUtf8Bytes(document)));
    }

    [Fact]
    public void IncomingTimestampsRequireAnExplicitOffset()
    {
        JsonObject document = JsonNode.Parse(Fixture("held.json"))!.AsObject();
        document["occurredAt"] = "2026-01-12T11:01:00";
        Assert.Throws<JsonException>(() => HoldEventWire.Read(JsonSerializer.SerializeToUtf8Bytes(document)));

        document["occurredAt"] = "2026-01-12T11:01:00+01:00";
        HoldLifecycleEvent offset = HoldEventWire.Read(JsonSerializer.SerializeToUtf8Bytes(document));
        Assert.Equal(TimeSpan.FromHours(1), offset.OccurredAt.Offset);
        Assert.Equal(HoldEventWire.Read(Fixture("held.json")).OccurredAt, offset.OccurredAt);

        document["occurredAt"] = "2026-01-12T10:01:00Z";
        Assert.Equal(TimeSpan.Zero, HoldEventWire.Read(JsonSerializer.SerializeToUtf8Bytes(document)).OccurredAt.Offset);
    }

    [Fact]
    public void ContractExamplesConvergeAfterDuplicateAndReorderedDelivery()
    {
        using var scenarios = JsonDocument.Parse(Fixture("delivery-orders.json"));
        foreach (JsonElement scenario in scenarios.RootElement.EnumerateArray())
        {
            // A reader-side oracle for the wire contract. PickupDesk supplies the durable consumer.
            var lastSeen = new Dictionary<Guid, HoldLifecycleEvent>();
            var counts = new List<int>();
            foreach (JsonElement file in scenario.GetProperty("delivery").EnumerateArray())
            {
                HoldLifecycleEvent message = HoldEventWire.Read(Fixture(file.GetString()!));
                if (!lastSeen.TryGetValue(message.Data.HoldId, out HoldLifecycleEvent? current) ||
                    message.Sequence > current.Sequence)
                {
                    lastSeen[message.Data.HoldId] = message;
                }

                counts.Add(lastSeen.Values.Count(snapshot => snapshot.Data.State == "held"));
            }

            Assert.Equal(scenario.GetProperty("queueCounts").EnumerateArray().Select(count => count.GetInt32()), counts);
        }
    }

    [Fact]
    public async Task CatalogContractSuppliesDescriptionForEnrollment()
    {
        using var document = JsonDocument.Parse(Fixture("http-examples.json"));
        JsonElement body = Example(document, "catalog-lookup").GetProperty("response").GetProperty("body");
        var item = new CatalogItemDescription(
            body.GetProperty("id").GetGuid(),
            body.GetProperty("title").GetString()!,
            body.GetProperty("description").GetString()!);
        ICatalogLookup catalog = new FixtureCatalog(item);
        HoldLifecycleEvent held = HoldEventWire.Read(Fixture("held.json"));

        CatalogItemDescription? found = await catalog.GetItemAsync(held.Data.CatalogItemId, CancellationToken.None);
        Assert.NotNull(found);
        Assert.Equal(held.Data.ItemTitle, found.Title);
        Assert.Null(await catalog.GetItemAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public void HttpExamplesDistinguishConflictOwnershipAndTheStaffQueue()
    {
        using var document = JsonDocument.Parse(Fixture("http-examples.json"));
        JsonElement conflict = Example(document, "losing-reservation").GetProperty("response");
        Assert.Equal(409, conflict.GetProperty("status").GetInt32());
        Assert.Equal(409, conflict.GetProperty("body").GetProperty("status").GetInt32());
        Assert.Equal("copy-unavailable", conflict.GetProperty("body").GetProperty("code").GetString());
        Assert.Equal(
            "urn:equipment-library:problem:copy-unavailable",
            conflict.GetProperty("body").GetProperty("type").GetString());
        JsonElement owner = Example(document, "owner-read");
        Assert.Equal("borrower-a", owner.GetProperty("actor").GetString());
        JsonElement ownedView = owner.GetProperty("response").GetProperty("body");
        Assert.Equal("held", ownedView.GetProperty("state").GetString());
        Assert.Equal(HoldEventWire.Read(Fixture("held.json")).Data.HoldId, ownedView.GetProperty("holdId").GetGuid());
        Assert.Equal(403, Example(document, "foreign-read").GetProperty("response").GetProperty("status").GetInt32());
        Assert.Equal(401, Example(document, "unauthenticated-read").GetProperty("response").GetProperty("status").GetInt32());
        JsonElement queue = Example(document, "queue-read");
        Assert.Equal("pickup-staff", queue.GetProperty("actor").GetString());
        JsonElement entry = Assert.Single(queue.GetProperty("response").GetProperty("body").GetProperty("items").EnumerateArray());
        Assert.Equal(ownedView.GetProperty("copyId").GetGuid(), entry.GetProperty("copyId").GetGuid());
        Assert.False(entry.TryGetProperty("borrowerId", out _));
        Assert.Equal(403, Example(document, "borrower-queue").GetProperty("response").GetProperty("status").GetInt32());
    }

    private static JsonElement Example(JsonDocument examples, string name) =>
        examples.RootElement.EnumerateArray().Single(entry => entry.GetProperty("name").GetString() == name);

    private sealed class FixtureCatalog(CatalogItemDescription item) : ICatalogLookup
    {
        public Task<CatalogItemDescription?> GetItemAsync(Guid itemId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(itemId == item.Id ? item : null);
        }
    }
}
