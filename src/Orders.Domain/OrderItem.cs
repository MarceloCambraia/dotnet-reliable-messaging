namespace Orders.Domain;

public sealed class OrderItem
{
    private OrderItem()
    {
        Sku = string.Empty;
    }

    internal OrderItem(string sku, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainException("SKU is required.");
        }

        if (quantity <= 0)
        {
            throw new DomainException($"Quantity for SKU '{sku}' must be greater than zero.");
        }

        if (unitPrice < 0)
        {
            throw new DomainException($"Unit price for SKU '{sku}' cannot be negative.");
        }

        Sku = sku.Trim().ToUpperInvariant();
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public string Sku { get; private set; }

    public int Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public decimal Subtotal => Quantity * UnitPrice;
}
