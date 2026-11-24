using EquipmentLibrary.Lending.Domain;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

public sealed class HoldPickupWindowTests
{
    [Fact]
    public void CollectionEndsAtTheSameInstantInEveryOffset()
    {
        var placedAt = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(2));
        var window = new HoldPickupWindow(placedAt);
        var deadline = new DateTimeOffset(2026, 10, 26, 11, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal(deadline, window.Deadline);
        Assert.False(window.CanCollectAt(placedAt.AddTicks(-1)));
        Assert.True(window.CanCollectAt(placedAt));
        Assert.True(window.CanCollectAt(deadline.AddTicks(-1)));
        Assert.False(window.CanCollectAt(deadline));
        Assert.False(window.CanCollectAt(deadline.AddTicks(1)));
        Assert.Equal(placedAt, HoldPickupWindow.LatestExpiredPlacement(deadline));
    }
}
