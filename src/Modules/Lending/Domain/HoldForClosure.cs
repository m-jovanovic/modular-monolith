namespace EquipmentLibrary.Lending.Domain;

public sealed record HoldForClosure(Guid BorrowerId, string State, HoldPickupWindow Window)
{
    public HoldClosureDecision Cancel(Guid requestingBorrower)
    {
        if (BorrowerId != requestingBorrower)
        {
            return new HoldClosureDecision.Refused(HoldClosureRefusal.OtherBorrower);
        }

        return State == "held"
            ? new HoldClosureDecision.Approved(HoldClosureAction.Cancel)
            : new HoldClosureDecision.Refused(HoldClosureRefusal.AlreadyClosed);
    }

    public HoldClosureDecision Collect(DateTimeOffset instant)
    {
        if (State != "held")
        {
            return new HoldClosureDecision.Refused(HoldClosureRefusal.AlreadyClosed);
        }

        return Window.CanCollectAt(instant)
            ? new HoldClosureDecision.Approved(HoldClosureAction.Collect)
            : new HoldClosureDecision.Refused(HoldClosureRefusal.PickupWindowClosed);
    }
}

public abstract record HoldClosureDecision
{
    private HoldClosureDecision() { }
    public sealed record Approved(HoldClosureAction Action) : HoldClosureDecision;
    public sealed record Refused(HoldClosureRefusal Reason) : HoldClosureDecision;
}

public enum HoldClosureAction { Cancel, Collect }
public enum HoldClosureRefusal { OtherBorrower, AlreadyClosed, PickupWindowClosed }
