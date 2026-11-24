using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using EquipmentLibrary.Lending.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace EquipmentLibrary.Lending.Delivery;

internal sealed partial class HoldRelay(OutboxStore store, HoldSubscriptions subscriptions, ILogger<HoldRelay> logger)
{
    private static readonly ActivitySource Activities = new("EquipmentLibrary.Lending.Outbox");
    private static readonly Meter Meter = new("EquipmentLibrary.Lending.Outbox");
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("equipment_library.outbox.deliveries");

    internal async Task<bool> DeliverNextAsync(CancellationToken cancellationToken)
    {
        PendingDelivery? item = await store.ClaimAsync(cancellationToken);
        if (item is null)
        {
            return false;
        }

        ActivityContext.TryParse(item.TraceParent, null, out ActivityContext parent);
        using Activity? activity = Activities.StartActivity("deliver hold", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.message.id", item.Id);
        try
        {
            HoldLifecycleEvent message = HoldEventWire.Read(Encoding.UTF8.GetBytes(item.Payload));
            if (message.MessageId != item.Id || item.Subscribers.Length == 0)
            {
                throw new InvalidDataException("Invalid outbox message identity or subscriber list.");
            }

            foreach (string name in item.Subscribers)
            {
                await subscriptions.Resolve(name).AcceptAsync(message, cancellationToken);
            }

            await store.CompleteAsync(item, cancellationToken);
            Deliveries.Add(1, new KeyValuePair<string, object?>("outcome", "acknowledged"));
            Acknowledged(logger, item.Id, message.Data.HoldId);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            await store.FailAsync(item, exception, cancellationToken);
            Deliveries.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            DeliveryFailed(logger, item.Id, item.Failures + 1, exception);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Acknowledged hold message {MessageId} for hold {HoldId}")]
    private static partial void Acknowledged(ILogger logger, Guid messageId, Guid holdId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not deliver hold message {MessageId} on attempt {Attempt}")]
    private static partial void DeliveryFailed(ILogger logger, Guid messageId, int attempt, Exception exception);
}
