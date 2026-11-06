namespace BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Processes one delivered message. Returning normally acks the message; throwing schedules a
/// retry (or dead-letters it once retries are exhausted). Throwing
/// <see cref="InvalidMessageException"/> dead-letters immediately.
/// Delivery is at-least-once, so implementations must be idempotent.
/// </summary>
public interface IMessageHandler
{
    Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken);
}
