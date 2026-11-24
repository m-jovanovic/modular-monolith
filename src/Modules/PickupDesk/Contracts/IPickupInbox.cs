using EquipmentLibrary.Lending.IntegrationEvents;

namespace EquipmentLibrary.PickupDesk.Contracts;

public interface IPickupInbox
{
    Task AcceptAsync(HoldLifecycleEvent message, CancellationToken cancellationToken);
}
