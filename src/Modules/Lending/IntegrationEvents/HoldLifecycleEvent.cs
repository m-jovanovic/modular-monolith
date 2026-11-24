namespace EquipmentLibrary.Lending.IntegrationEvents;

public sealed record HoldLifecycleEvent(
    Guid MessageId,
    string EventType,
    int SchemaVersion,
    long Sequence,
    DateTimeOffset OccurredAt,
    HoldSnapshot Data)
{
    public const string Placed = "lending.hold-placed";
    public const string Cancelled = "lending.hold-cancelled";
    public const string Collected = "lending.hold-collected";
}

public sealed record HoldSnapshot(
    Guid HoldId,
    Guid CopyId,
    Guid CatalogItemId,
    string Barcode,
    string State,
    string? ItemTitle = null);
