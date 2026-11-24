using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquipmentLibrary.Lending.IntegrationEvents;

public static class HoldEventWire
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new OffsetTimestampConverter() }
    };

    public static HoldLifecycleEvent Read(ReadOnlySpan<byte> json)
    {
        HoldLifecycleEvent message = JsonSerializer.Deserialize<HoldLifecycleEvent>(json, Options)
            ?? throw new JsonException("Expected a hold event object.");
        Validate(message);
        return message;
    }

    public static byte[] Write(HoldLifecycleEvent message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Validate(message);
        return JsonSerializer.SerializeToUtf8Bytes(message, Options);
    }

    private static void Validate(HoldLifecycleEvent message)
    {
        if (message.SchemaVersion != 1)
        {
            throw new JsonException("Unsupported hold schemaVersion.");
        }

        if (message.MessageId == Guid.Empty || message.Data is null ||
            message.Data.HoldId == Guid.Empty || message.Data.CopyId == Guid.Empty ||
            message.Data.CatalogItemId == Guid.Empty || string.IsNullOrWhiteSpace(message.Data.Barcode))
        {
            throw new JsonException("A hold event requires nonempty IDs and a barcode.");
        }

        bool matchesTransition = (message.EventType, message.Data.State, message.Sequence) switch
        {
            (HoldLifecycleEvent.Placed, "held", 1) => true,
            (HoldLifecycleEvent.Cancelled, "cancelled", > 1) => true,
            (HoldLifecycleEvent.Collected, "collected", > 1) => true,
            _ => false
        };

        if (!matchesTransition)
        {
            throw new JsonException("The event type, state and per-hold sequence must describe a supported transition.");
        }
    }

    private sealed class OffsetTimestampConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Expected an offset-aware date-time string.");
            }

            string value = reader.GetString()!;
            bool explicitOffset = value.EndsWith('Z') ||
                (value.Length >= 6 && value[^6] is '+' or '-' && value[^3] == ':');
            if (!explicitOffset || !reader.TryGetDateTimeOffset(out DateTimeOffset instant))
            {
                throw new JsonException("occurredAt must include Z or an explicit offset such as +01:00.");
            }

            return instant;
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }
}
