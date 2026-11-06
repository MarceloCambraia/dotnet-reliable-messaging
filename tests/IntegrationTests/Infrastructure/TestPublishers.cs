using System.Collections.Concurrent;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.RabbitMq;

namespace IntegrationTests.Infrastructure;

/// <summary>Records what would have been published; a small delay forces concurrent batches to overlap.</summary>
public sealed class RecordingPublisher(TimeSpan delayPerMessage) : IMessagePublisher
{
    public ConcurrentQueue<Guid> Published { get; } = new();

    public async Task PublishAsync(string exchange, string routingKey, MessageEnvelope envelope, string? parentTraceParent, CancellationToken cancellationToken)
    {
        await Task.Delay(delayPerMessage, cancellationToken);
        Published.Enqueue(envelope.MessageId);
    }
}

/// <summary>Always fails with the exception produced by the factory.</summary>
public sealed class FailingPublisher(Func<Exception> error) : IMessagePublisher
{
    public int Calls { get; private set; }

    public Task PublishAsync(string exchange, string routingKey, MessageEnvelope envelope, string? parentTraceParent, CancellationToken cancellationToken)
    {
        Calls++;
        throw error();
    }
}
