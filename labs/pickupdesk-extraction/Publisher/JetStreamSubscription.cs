using EquipmentLibrary.Lab.JetStream;
using EquipmentLibrary.Lending.Delivery;
using EquipmentLibrary.Lending.IntegrationEvents;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace EquipmentLibrary.Lab.Publisher;

internal sealed class JetStreamSubscription(NatsJSContext jetStream) : IHoldSubscriber
{
    public string Name => "pickup-desk.queue.v1";

    public async Task AcceptAsync(HoldLifecycleEvent message, CancellationToken cancellationToken)
    {
        PubAckResponse acknowledgement = await jetStream.PublishAsync(HoldTransport.Subject, HoldEventWire.Write(message),
            opts: new NatsJSPubOpts { MsgId = message.MessageId.ToString() }, cancellationToken: cancellationToken);
        acknowledgement.EnsureSuccess();
    }
}
