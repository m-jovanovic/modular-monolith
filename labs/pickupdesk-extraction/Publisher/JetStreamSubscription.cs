using System.Diagnostics;
using EquipmentLibrary.Lab.JetStream;
using EquipmentLibrary.Lending.Delivery;
using EquipmentLibrary.Lending.IntegrationEvents;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace EquipmentLibrary.Lab.Publisher;

internal sealed class JetStreamSubscription(NatsJSContext jetStream) : IHoldSubscriber
{
    public string Name => "pickup-desk.queue.v1";

    public async Task AcceptAsync(HoldLifecycleEvent message, CancellationToken cancellationToken)
    {
        NatsHeaders? headers = Activity.Current is { IdFormat: ActivityIdFormat.W3C, Id: string parent }
            ? new NatsHeaders { ["traceparent"] = parent }
            : null;
        PubAckResponse acknowledgement = await jetStream.PublishAsync(HoldTransport.Subject, HoldEventWire.Write(message),
            opts: new NatsJSPubOpts { MsgId = message.MessageId.ToString() }, headers: headers, cancellationToken: cancellationToken);
        acknowledgement.EnsureSuccess();
    }
}
