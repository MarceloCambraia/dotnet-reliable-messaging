namespace Orders.Domain;

public sealed class Order
{
    private readonly List<OrderItem> _items = [];

    private Order()
    {
        CustomerId = string.Empty;
    }

    private Order(Guid id, string customerId, DateTimeOffset createdAt)
    {
        Id = id;
        CustomerId = customerId;
        CreatedAt = createdAt;
        Status = OrderStatus.Placed;
    }

    public Guid Id { get; private set; }

    public string CustomerId { get; private set; }

    public OrderStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;

    public decimal Total => _items.Sum(i => i.Subtotal);

    public static Order Place(string customerId, IReadOnlyCollection<OrderLine> lines, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(clock);

        if (string.IsNullOrWhiteSpace(customerId))
        {
            throw new DomainException("Customer is required.");
        }

        if (lines.Count == 0)
        {
            throw new DomainException("An order must have at least one item.");
        }

        var order = new Order(Guid.CreateVersion7(), customerId.Trim(), clock.GetUtcNow());

        foreach (var line in lines)
        {
            var item = new OrderItem(line.Sku, line.Quantity, line.UnitPrice);
            if (order._items.Exists(i => i.Sku == item.Sku))
            {
                throw new DomainException($"SKU '{item.Sku}' appears more than once.");
            }

            order._items.Add(item);
        }

        return order;
    }
}

public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice);
