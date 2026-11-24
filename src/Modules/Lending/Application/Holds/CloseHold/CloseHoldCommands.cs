using EquipmentLibrary.Lending.Domain;

namespace EquipmentLibrary.Lending.Application.Holds.CloseHold;

public sealed record CancelHoldCommand(Guid HoldId, Guid BorrowerId);
public sealed record CollectHoldCommand(Guid HoldId);

public abstract record CloseHoldResult
{
    private CloseHoldResult() { }
    public sealed record Closed(Guid HoldId, Guid CopyId, string State, HoldPickupWindow Window) : CloseHoldResult;
    public sealed record Refused(HoldClosureRefusal Reason) : CloseHoldResult;
    public sealed record MissingHold : CloseHoldResult;
}

public interface IHoldClosureStore
{
    // Decide against the current hold and clock while holding the transaction's row lock.
    Task<CloseHoldResult> TryCloseAsync(Guid holdId, Func<HoldForClosure, DateTimeOffset, HoldClosureDecision> decide,
        CancellationToken cancellationToken);
}

public sealed class CloseHoldCommandHandler(IHoldClosureStore holds)
{
    public Task<CloseHoldResult> HandleAsync(CancelHoldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return holds.TryCloseAsync(command.HoldId, (hold, _) => hold.Cancel(command.BorrowerId), cancellationToken);
    }

    public Task<CloseHoldResult> HandleAsync(CollectHoldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return holds.TryCloseAsync(command.HoldId, (hold, instant) => hold.Collect(instant), cancellationToken);
    }
}
