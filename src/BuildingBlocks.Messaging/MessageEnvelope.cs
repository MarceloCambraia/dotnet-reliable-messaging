using System.Text.Json;
using System.Text.Json.Serialization;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Broker-agnostic wrapper around every message on the wire. The identifiers travel in the
/// body (so the message is self-describing) and are mirrored in AMQP properties (so they
/// are visible in tooling such as the RabbitMQ management UI).
/// </summary>
public sealed record MessageEnvelope
{
    /// <summary>Unique id of this message. Consumers use it as the idempotency key.</summary>
    public required Guid MessageId { get; init; }

    /// <summary>Versioned message type, e.g. <c>orders.order-created.v1</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Groups every message caused by the same business operation.</summary>
    public required string CorrelationId { get; init; }

    /// <summary>When the business fact happened (not when it was published).</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    public required JsonElement Payload { get; init; }

    public static MessageEnvelope Create<TPayload>(
        Guid messageId,
        string type,
        string correlationId,
        DateTimeOffset occurredAt,
        TPayload payload)
        where TPayload : notnull =>
        new()
        {
            MessageId = messageId,
            Type = type,
            CorrelationId = correlationId,
            OccurredAt = occurredAt,
            Payload = JsonSerializer.SerializeToElement(payload, EnvelopeSerializer.Options),
        };

    public TPayload GetPayload<TPayload>()
    {
        try
        {
            return Payload.Deserialize<TPayload>(EnvelopeSerializer.Options)
                ?? throw new InvalidMessageException($"Payload of message {MessageId} is null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidMessageException($"Payload of message {MessageId} is not a valid {typeof(TPayload).Name}.", ex);
        }
    }
}

public static class EnvelopeSerializer
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] Serialize(MessageEnvelope envelope) =>
        JsonSerializer.SerializeToUtf8Bytes(envelope, Options);

    /// <exception cref="InvalidMessageException">The body is not a valid envelope.</exception>
    public static MessageEnvelope Deserialize(ReadOnlySpan<byte> body)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<MessageEnvelope>(body, Options);
            if (envelope is null || envelope.MessageId == Guid.Empty || string.IsNullOrWhiteSpace(envelope.Type))
            {
                throw new InvalidMessageException("Message body is not a valid envelope.");
            }

            return envelope;
        }
        catch (JsonException ex)
        {
            throw new InvalidMessageException("Message body is not valid JSON.", ex);
        }
    }
}

/// <summary>
/// A message that can never be processed (poison message). Retrying is pointless, so it goes
/// straight to the dead-letter queue.
/// </summary>
public sealed class InvalidMessageException : Exception
{
    public InvalidMessageException(string message)
        : base(message)
    {
    }

    public InvalidMessageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
