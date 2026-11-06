namespace Orders.Contracts;

/// <summary>
/// Integration event published when an order is placed. This is the public contract
/// shared with consumers; it never exposes the domain model directly.
/// </summary>
public sealed record OrderCreated(
    Guid OrderId,
    string CustomerId,
    IReadOnlyList<OrderCreatedItem> Items,
    decimal Total,
    DateTimeOffset PlacedAt)
{
    /// <summary>Versioned message type; also used as the RabbitMQ routing key.</summary>
    public const string MessageType = "orders.order-created.v1";
}

public sealed record OrderCreatedItem(string Sku, int Quantity);
