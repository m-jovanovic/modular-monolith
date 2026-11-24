using EquipmentLibrary.Lending.Domain;

namespace EquipmentLibrary.Lending.Application.Holds.PlaceHold;

public sealed record PlaceHoldCommand(Guid CopyId, Guid BorrowerId);

public abstract record PlaceHoldResult
{
    private PlaceHoldResult() { }

    public sealed record Placed(Guid HoldId, Guid CopyId, HoldPickupWindow Window) : PlaceHoldResult;
    public sealed record InvalidCopy : PlaceHoldResult;
    public sealed record MissingCopy : PlaceHoldResult;
    public sealed record Unavailable : PlaceHoldResult;
}

public interface IHoldReservationStore
{
    Task<PlaceHoldResult> TryPlaceAsync(
        PlaceHoldCommand command, Guid holdId, DateTimeOffset placedAt, CancellationToken cancellationToken);
}

public sealed class PlaceHoldCommandHandler(IHoldReservationStore reservations, TimeProvider clock)
{
    public Task<PlaceHoldResult> HandleAsync(PlaceHoldCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.CopyId == Guid.Empty)
        {
            return Task.FromResult<PlaceHoldResult>(new PlaceHoldResult.InvalidCopy());
        }

        ArgumentOutOfRangeException.ThrowIfEqual(command.BorrowerId, Guid.Empty);
        return reservations.TryPlaceAsync(command, Guid.CreateVersion7(), clock.GetUtcNow(), cancellationToken);
    }
}
