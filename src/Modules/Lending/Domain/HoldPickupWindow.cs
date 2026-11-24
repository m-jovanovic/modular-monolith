namespace EquipmentLibrary.Lending.Domain;

public readonly record struct HoldPickupWindow(DateTimeOffset PlacedAt)
{
    private static readonly TimeSpan Duration = TimeSpan.FromHours(48);

    public DateTimeOffset Deadline => PlacedAt + Duration;

    public bool CanCollectAt(DateTimeOffset instant) => instant >= PlacedAt && instant < Deadline;

    public static DateTimeOffset LatestExpiredPlacement(DateTimeOffset instant) => instant - Duration;
}
