using EquipmentLibrary.Lending.Delivery;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.PickupDesk.Contracts;

namespace EquipmentLibrary.Api.Bootstrap;

internal sealed class PickupSubscription(IPickupInbox inbox) : IHoldSubscriber
{
    public string Name => "pickup-desk.queue.v1";

    public Task AcceptAsync(HoldLifecycleEvent message, CancellationToken cancellationToken) =>
        inbox.AcceptAsync(message, cancellationToken);
}
