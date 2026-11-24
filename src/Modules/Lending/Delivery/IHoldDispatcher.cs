namespace EquipmentLibrary.Lending.Delivery;

public enum HoldDispatchOutcome
{
    Idle,
    Delivered,
    Failed,
    LeaseLost
}

public interface IHoldDispatcher
{
    Task<HoldDispatchOutcome> DeliverNextAsync(CancellationToken cancellationToken);
}
