using EquipmentLibrary.Lab.JetStream;
using EquipmentLibrary.Lending.IntegrationEvents;
using EquipmentLibrary.PickupDesk.Contracts;
using NATS.Client.JetStream;

namespace EquipmentLibrary.Lab.PickupService;

internal sealed class ReceiveHoldMessages(NatsJSContext jetStream, IPickupInbox inbox) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        INatsJSConsumer consumer = await jetStream.GetConsumerAsync(HoldStream.Stream, HoldStream.Consumer, stoppingToken);
        await foreach (NatsJSMsg<byte[]> message in consumer.ConsumeAsync<byte[]>(cancellationToken: stoppingToken))
        {
            HoldLifecycleEvent envelope = HoldEventWire.Read(message.Data ?? throw new InvalidDataException("Empty hold envelope."));
            await inbox.AcceptAsync(envelope, stoppingToken);
            await message.AckAsync(cancellationToken: stoppingToken);
        }
    }
}
