using NSubstitute;
using Orders.Application.Abstractions;
using Orders.Application.Orders;
using Orders.Contracts;
using Orders.Domain;
using UnitTests.TestDoubles;

namespace UnitTests.Orders;

public sealed class CreateOrderHandlerTests
{
    private readonly IOrderRepository _orders = Substitute.For<IOrderRepository>();
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 3, 1, 9, 30, 0, TimeSpan.Zero));

    private CreateOrderHandler CreateSut() => new(_orders, _outbox, _unitOfWork, _clock);

    [Fact]
    public async Task Handle_stages_order_and_event_then_commits_once()
    {
        var command = new CreateOrderCommand("customer-42", [new CreateOrderItem("SKU-1", 3, 2m)]);

        var orderId = await CreateSut().Handle(command, CancellationToken.None);

        _orders.Received(1).Add(Arg.Is<Order>(o => o.Id == orderId && o.CustomerId == "customer-42"));
        _outbox.Received(1).Enqueue(
            OrderCreated.MessageType,
            Arg.Is<OrderCreated>(e =>
                e.OrderId == orderId &&
                e.CustomerId == "customer-42" &&
                e.Total == 6m &&
                e.PlacedAt == _clock.Now &&
                e.Items.Count == 1 && e.Items[0].Sku == "SKU-1" && e.Items[0].Quantity == 3));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_commits_after_staging_both_the_aggregate_and_the_message()
    {
        var command = new CreateOrderCommand("c", [new CreateOrderItem("A", 1, 1m)]);

        await CreateSut().Handle(command, CancellationToken.None);

        Received.InOrder(() =>
        {
            _orders.Add(Arg.Any<Order>());
            _outbox.Enqueue(Arg.Any<string>(), Arg.Any<OrderCreated>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Handle_with_invalid_order_stages_nothing()
    {
        var command = new CreateOrderCommand("c", []);

        var act = () => CreateSut().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<DomainException>();
        _orders.DidNotReceiveWithAnyArgs().Add(default!);
        _outbox.DidNotReceiveWithAnyArgs().Enqueue<OrderCreated>(default!, default!);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
