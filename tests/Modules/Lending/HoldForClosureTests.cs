using EquipmentLibrary.Lending.Domain;
using Xunit;

namespace EquipmentLibrary.Lending.Tests;

public sealed class HoldForClosureTests
{
    private static readonly HoldPickupWindow Window = new(DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("held")]
    [InlineData("cancelled")]
    [InlineData("collected")]
    public void AnotherBorrowerCannotDiscoverTheHoldStateByCancelling(string state)
    {
        var hold = new HoldForClosure(Guid.NewGuid(), state, Window);
        Assert.Equal(new HoldClosureDecision.Refused(HoldClosureRefusal.OtherBorrower), hold.Cancel(Guid.NewGuid()));
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("collected")]
    public void ClosedHoldsRejectBothCommandsEvenAfterTheDeadline(string state)
    {
        var hold = new HoldForClosure(Guid.NewGuid(), state, Window);
        var refused = new HoldClosureDecision.Refused(HoldClosureRefusal.AlreadyClosed);
        Assert.Equal(refused, hold.Cancel(hold.BorrowerId));
        Assert.Equal(refused, hold.Collect(Window.Deadline));
    }

    [Fact]
    public void AnActiveHoldCanBeCancelledByItsOwnerAndCollectedOnlyDuringItsWindow()
    {
        var hold = new HoldForClosure(Guid.NewGuid(), "held", Window);
        var collected = new HoldClosureDecision.Approved(HoldClosureAction.Collect);
        var tooLate = new HoldClosureDecision.Refused(HoldClosureRefusal.PickupWindowClosed);
        Assert.Equal(new HoldClosureDecision.Approved(HoldClosureAction.Cancel), hold.Cancel(hold.BorrowerId));
        Assert.Equal(collected, hold.Collect(Window.PlacedAt));
        Assert.Equal(collected, hold.Collect(Window.Deadline.AddTicks(-1)));
        Assert.Equal(tooLate, hold.Collect(Window.Deadline));
    }
}
