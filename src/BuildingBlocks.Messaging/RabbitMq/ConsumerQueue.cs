using RabbitMQ.Client;

namespace BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Describes a consumer queue and the retry / dead-letter topology around it:
/// <code>
///   exchange ──(routing keys)──► {name} ──consumer──► ack
///                                  │  ▲
///          handler failed, retry n │  │ TTL expires (dead-lettered back)
///                                  ▼  │
///                            {name}.retry.{n}        (one queue per delay tier)
///
///   retries exhausted / poison ──► {name}.dlx ──► {name}.dlq
/// </code>
/// One retry queue per delay avoids head-of-line blocking: RabbitMQ only expires messages
/// at the head of a queue, so mixing different TTLs in one queue would delay short ones.
/// </summary>
public sealed record ConsumerQueue(
    string Name,
    string Exchange,
    IReadOnlyList<string> RoutingKeys,
    IReadOnlyList<TimeSpan> RetryDelays)
{
    public string DeadLetterExchange => $"{Name}.dlx";

    public string DeadLetterQueue => $"{Name}.dlq";

    /// <summary>Number of retries after the first delivery; total attempts = MaxRetries + 1.</summary>
    public int MaxRetries => RetryDelays.Count;

    /// <param name="retry">1-based retry number.</param>
    public string RetryQueue(int retry) => $"{Name}.retry.{retry}";

    /// <summary>Idempotently declares the whole topology. Safe to call on every (re)connect.</summary>
    public async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: QuorumQueue(),
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, Name, cancellationToken: cancellationToken);

        // Messages rejected with requeue=false (e.g. poison messages) are dead-lettered natively by the broker.
        var mainArguments = QuorumQueue();
        mainArguments["x-dead-letter-exchange"] = DeadLetterExchange;
        mainArguments["x-dead-letter-routing-key"] = Name;
        await channel.QueueDeclareAsync(
            Name,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: mainArguments,
            cancellationToken: cancellationToken);

        foreach (var routingKey in RoutingKeys)
        {
            await channel.QueueBindAsync(Name, Exchange, routingKey, cancellationToken: cancellationToken);
        }

        for (var retry = 1; retry <= MaxRetries; retry++)
        {
            // When the TTL expires the message is dead-lettered through the default exchange
            // straight back into the main queue.
            var retryArguments = QuorumQueue();
            retryArguments["x-message-ttl"] = (long)RetryDelays[retry - 1].TotalMilliseconds;
            retryArguments["x-dead-letter-exchange"] = string.Empty;
            retryArguments["x-dead-letter-routing-key"] = Name;
            await channel.QueueDeclareAsync(
                RetryQueue(retry),
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: retryArguments,
                cancellationToken: cancellationToken);
        }
    }

    // Quorum queues are the replicated, durable queue type recommended since RabbitMQ 3.8
    // (classic mirrored queues were removed in 4.0).
    private static Dictionary<string, object?> QuorumQueue() => new() { ["x-queue-type"] = "quorum" };
}
