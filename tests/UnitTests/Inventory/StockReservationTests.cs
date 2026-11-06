using Inventory.Worker.Domain;
using Orders.Contracts;

namespace UnitTests.Inventory;

public sealed class StockReservationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Reserves_every_line_when_stock_is_sufficient()
    {
        var products = Catalog(("A", 10), ("B", 5));

        var reservation = StockReservation.ForOrder(Guid.NewGuid(), [new OrderCreatedItem("A", 3), new OrderCreatedItem("B", 5)], products, Now);

        reservation.Status.Should().Be(ReservationStatus.Reserved);
        reservation.RejectionReason.Should().BeNull();
        products["A"].AvailableQuantity.Should().Be(7);
        products["A"].ReservedQuantity.Should().Be(3);
        products["B"].AvailableQuantity.Should().Be(0);
    }

    [Fact]
    public void Rejects_the_whole_order_and_touches_no_stock_when_one_line_is_short()
    {
        var products = Catalog(("A", 10), ("B", 1));

        var reservation = StockReservation.ForOrder(Guid.NewGuid(), [new OrderCreatedItem("A", 3), new OrderCreatedItem("B", 2)], products, Now);

        reservation.Status.Should().Be(ReservationStatus.Rejected);
        reservation.RejectionReason.Should().Contain("Insufficient stock for B");
        products["A"].AvailableQuantity.Should().Be(10, "reservations are all-or-nothing");
        products["B"].AvailableQuantity.Should().Be(1);
    }

    [Fact]
    public void Rejects_unknown_skus()
    {
        var reservation = StockReservation.ForOrder(Guid.NewGuid(), [new OrderCreatedItem("NOPE", 1)], Catalog(), Now);

        reservation.Status.Should().Be(ReservationStatus.Rejected);
        reservation.RejectionReason.Should().Be("Unknown SKU NOPE.");
    }

    [Fact]
    public void Product_refuses_to_go_below_zero()
    {
        var product = Product.Create("A", "a", 1);

        var act = () => product.Reserve(2);

        act.Should().Throw<InvalidOperationException>();
        product.CanReserve(0).Should().BeFalse();
    }

    private static Dictionary<string, Product> Catalog(params (string Sku, int Available)[] items) =>
        items.ToDictionary(i => i.Sku, i => Product.Create(i.Sku, i.Sku, i.Available), StringComparer.Ordinal);
}
