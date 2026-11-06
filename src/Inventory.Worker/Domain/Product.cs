namespace Inventory.Worker.Domain;

public sealed class Product
{
    public required string Sku { get; init; }

    public required string Name { get; init; }

    public int AvailableQuantity { get; private set; }

    public int ReservedQuantity { get; private set; }

    public static Product Create(string sku, string name, int available) =>
        new() { Sku = sku, Name = name, AvailableQuantity = available };

    public bool CanReserve(int quantity) => quantity > 0 && AvailableQuantity >= quantity;

    public void Reserve(int quantity)
    {
        if (!CanReserve(quantity))
        {
            throw new InvalidOperationException($"Cannot reserve {quantity} of {Sku}: only {AvailableQuantity} available.");
        }

        AvailableQuantity -= quantity;
        ReservedQuantity += quantity;
    }
}
