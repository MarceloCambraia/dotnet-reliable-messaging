using NSubstitute;
using Orders.Application.Abstractions;
using Orders.Application.Orders;
using Orders.Domain;
using UnitTests.TestDoubles;

namespace UnitTests.Orders;

public sealed class GetOrderHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();

    [Fact]
    public async Task Handle_maps_existing_order()
    {
        var order = Order.Place("c", [new OrderLine("A", 2, 3m)], new FixedTimeProvider(DateTimeOffset.UnixEpoch));
        _orders.FindAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var dto = await new GetOrderHandler(_orders).Handle(new GetOrderQuery(order.Id), CancellationToken.None);

        dto.Should().BeEquivalentTo(new OrderDto(
            order.Id, "c", "Placed", 6m, DateTimeOffset.UnixEpoch, [new OrderItemDto("A", 2, 3m)]));
    }

    [Fact]
    public async Task Handle_returns_null_when_order_does_not_exist()
    {
        var dto = await new GetOrderHandler(_orders).Handle(new GetOrderQuery(Guid.NewGuid()), CancellationToken.None);

        dto.Should().BeNull();
    }
}
