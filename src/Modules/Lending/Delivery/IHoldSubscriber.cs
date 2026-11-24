using EquipmentLibrary.Lending.IntegrationEvents;

namespace EquipmentLibrary.Lending.Delivery;

public interface IHoldSubscriber
{
    string Name { get; }
    Task AcceptAsync(HoldLifecycleEvent message, CancellationToken cancellationToken);
}
