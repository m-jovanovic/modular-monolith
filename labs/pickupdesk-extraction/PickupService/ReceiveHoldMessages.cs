using System.Diagnostics;
using EquipmentLibrary.Lab.JetStream;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.PickupDesk.Contracts;
using NATS.Client.JetStream;

namespace EquipmentLibrary.Lab.PickupService;

internal sealed class ReceiveHoldMessages(NatsJSContext jetStream, IPickupInbox inbox) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        INatsJSConsumer consumer = await jetStream.GetConsumerAsync(HoldTransport.Stream, HoldTransport.Consumer, stoppingToken);
        await foreach (NatsJSMsg<byte[]> message in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            HoldLifecycleEvent envelope = HoldEventWire.Read(message.Data ?? throw new InvalidDataException("Empty hold envelope."));
            string? parent = message.Headers?["traceparent"].ToString();
            using Activity? receipt = ActivityContext.TryParse(parent, null, isRemote: true, out _)
                ? new Activity("receive broker hold").SetParentId(parent!).Start()
                : null;
            await inbox.AcceptAsync(envelope, stoppingToken);
            await message.AckAsync(cancellationToken: stoppingToken);
        }
    }
}
