namespace Inventory.Worker.Persistence;

/// <summary>
/// Record of a message whose side effects were committed. Inserted in the same transaction
/// as those effects, so "processed" and "effect applied" can never disagree.
/// </summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; init; }

    public required string Type { get; init; }

    public DateTimeOffset ProcessedAt { get; init; }
}
