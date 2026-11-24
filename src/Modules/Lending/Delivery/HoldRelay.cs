using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using EquipmentLibrary.Lending.IntegrationEvents;
using Microsoft.Extensions.Logging;

namespace EquipmentLibrary.Lending.Delivery;

internal sealed partial class HoldRelay(OutboxStore store, HoldSubscriptions subscriptions, ILogger<HoldRelay> logger) : IHoldDispatcher
{
    private static readonly ActivitySource Activities = new("EquipmentLibrary.Lending.Outbox");
    private static readonly Meter Meter = new("EquipmentLibrary.Lending.Outbox");
    private static readonly Counter<long> Deliveries = Meter.CreateCounter<long>("equipment_library.outbox.deliveries");

    public async Task<HoldDispatchOutcome> DeliverNextAsync(CancellationToken cancellationToken)
    {
        PendingDelivery? item = await store.ClaimAsync(cancellationToken);
        if (item is null)
        {
            return HoldDispatchOutcome.Idle;
        }

        bool hasParent = ActivityContext.TryParse(item.TraceParent, null, isRemote: true, out ActivityContext parent);
        using Activity? activity = Activities.StartActivity("deliver hold", ActivityKind.Producer, parent)
            ?? (hasParent ? new Activity("deliver hold").SetParentId(item.TraceParent!).Start() : null);
        activity?.SetTag("messaging.operation.type", "send");
        activity?.SetTag("messaging.message.id", item.Id.ToString());
        Guid? holdId = null;
        try
        {
            HoldLifecycleEvent message = HoldEventWire.Read(Encoding.UTF8.GetBytes(item.Payload));
            holdId = message.Data.HoldId;
            activity?.SetTag("library.hold.id", message.Data.HoldId.ToString());
            if (message.MessageId != item.Id || item.Subscribers.Length == 0)
            {
                throw new InvalidDataException("Invalid outbox message identity or subscriber list.");
            }

            foreach (string name in item.Subscribers)
            {
                await subscriptions.Resolve(name).AcceptAsync(message, cancellationToken);
            }

            if (!await store.CompleteAsync(item, cancellationToken))
            {
                activity?.SetTag("library.delivery.outcome", "lease-lost");
                return HoldDispatchOutcome.LeaseLost;
            }

            activity?.SetTag("library.delivery.outcome", "acknowledged");
            Deliveries.Add(1, new KeyValuePair<string, object?>("outcome", "acknowledged"));
            Acknowledged(logger, item.Id, message.Data.HoldId, activity?.TraceId);
            return HoldDispatchOutcome.Delivered;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            if (!await store.FailAsync(item, exception, cancellationToken))
            {
                activity?.SetTag("library.delivery.outcome", "lease-lost");
                return HoldDispatchOutcome.LeaseLost;
            }

            activity?.SetTag("library.delivery.outcome", "failed");
            Deliveries.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            DeliveryFailed(logger, item.Id, holdId, activity?.TraceId, item.Failures + 1, exception);
            return HoldDispatchOutcome.Failed;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Acknowledged hold message {MessageId} for hold {HoldId} in trace {TraceId}")]
    private static partial void Acknowledged(ILogger logger, Guid messageId, Guid holdId, ActivityTraceId? traceId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not deliver message {MessageId} for hold {HoldId} in trace {TraceId} on attempt {Attempt}")]
    private static partial void DeliveryFailed(ILogger logger, Guid messageId, Guid? holdId, ActivityTraceId? traceId, int attempt, Exception exception);
}
