using Orders.Domain;
using UnitTests.TestDoubles;

namespace UnitTests.Orders;

public sealed class OrderTests
{
    private static readonly FixedTimeProvider Clock = new(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Place_with_valid_lines_creates_placed_order_with_total()
    {
        var order = Order.Place(
            "customer-1",
            [new OrderLine("sku-1", 2, 10m), new OrderLine("SKU-2", 1, 5.5m)],
            Clock);

        order.Id.Should().NotBeEmpty();
        order.Status.Should().Be(OrderStatus.Placed);
        order.CreatedAt.Should().Be(Clock.Now);
        order.Total.Should().Be(25.5m);
        order.Items.Select(i => i.Sku).Should().Equal("SKU-1", "SKU-2");
    }

    [Fact]
    public void Place_generates_time_ordered_ids()
    {
        var first = Order.Place("c", [new OrderLine("A", 1, 1m)], Clock);
        var second = Order.Place("c", [new OrderLine("A", 1, 1m)], Clock);

        first.Id.Should().NotBe(second.Id);
        first.Id.Version.Should().Be(7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Place_without_customer_is_rejected(string customerId)
    {
        var act = () => Order.Place(customerId, [new OrderLine("A", 1, 1m)], Clock);

        act.Should().Throw<DomainException>().WithMessage("Customer is required.");
    }

    [Fact]
    public void Place_without_items_is_rejected()
    {
        var act = () => Order.Place("c", [], Clock);

        act.Should().Throw<DomainException>().WithMessage("*at least one item*");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Place_with_non_positive_quantity_is_rejected(int quantity)
    {
        var act = () => Order.Place("c", [new OrderLine("A", quantity, 1m)], Clock);

        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void Place_with_negative_price_is_rejected()
    {
        var act = () => Order.Place("c", [new OrderLine("A", 1, -0.01m)], Clock);

        act.Should().Throw<DomainException>().WithMessage("*cannot be negative*");
    }

    [Fact]
    public void Place_with_blank_sku_is_rejected()
    {
        var act = () => Order.Place("c", [new OrderLine(" ", 1, 1m)], Clock);

        act.Should().Throw<DomainException>().WithMessage("SKU is required.");
    }

    [Fact]
    public void Place_with_duplicated_sku_is_rejected_case_insensitively()
    {
        var act = () => Order.Place("c", [new OrderLine("abc", 1, 1m), new OrderLine("ABC", 2, 1m)], Clock);

        act.Should().Throw<DomainException>().WithMessage("*more than once*");
    }
}
