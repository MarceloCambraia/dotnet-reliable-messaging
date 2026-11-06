using Orders.Contracts;

namespace Inventory.Worker.Domain;

public enum ReservationStatus
{
    Reserved = 1,
    Rejected = 2,
}

public sealed class StockReservation
{
    private readonly List<ReservationLine> _lines = [];

    private StockReservation()
    {
    }

    public Guid OrderId { get; private set; }

    public ReservationStatus Status { get; private set; }

    public string? RejectionReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<ReservationLine> Lines => _lines;

    /// <summary>
    /// All-or-nothing: either every line is reserved or nothing is. Running out of stock is a
    /// business outcome (a rejected reservation), not an error, so it is never retried.
    /// </summary>
    public static StockReservation ForOrder(
        Guid orderId,
        IReadOnlyCollection<OrderCreatedItem> items,
        IReadOnlyDictionary<string, Product> products,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(products);

        var reservation = new StockReservation { OrderId = orderId, CreatedAt = now };
        reservation._lines.AddRange(items.Select(i => new ReservationLine(i.Sku, i.Quantity)));

        var problem = items
            .Select(item => products.TryGetValue(item.Sku, out var product)
                ? product.CanReserve(item.Quantity) ? null : $"Insufficient stock for {item.Sku}: requested {item.Quantity}, available {product.AvailableQuantity}."
                : $"Unknown SKU {item.Sku}.")
            .FirstOrDefault(p => p is not null);

        if (problem is not null)
        {
            reservation.Status = ReservationStatus.Rejected;
            reservation.RejectionReason = problem;
            return reservation;
        }

        foreach (var item in items)
        {
            products[item.Sku].Reserve(item.Quantity);
        }

        reservation.Status = ReservationStatus.Reserved;
        return reservation;
    }
}

public sealed record ReservationLine(string Sku, int Quantity);
