namespace Orders.Infrastructure.Outbox;

/// <summary>
/// A message waiting to be published. It is inserted in the same transaction as the business
/// change, so it exists if and only if the change was committed.
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>Also the published MessageId, i.e. the consumer-side idempotency key.</summary>
    public Guid Id { get; init; }

    public required string Type { get; init; }

    /// <summary>Serialized integration event (stored as <c>jsonb</c>).</summary>
    public required string Payload { get; init; }

    public required string CorrelationId { get; init; }

    /// <summary>W3C <c>traceparent</c> of the request that produced the message, so the publish span joins its trace.</summary>
    public string? TraceParent { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public int Attempts { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Set when the message exhausted its attempts; it then needs operator attention.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    public string? LastError { get; set; }
}
