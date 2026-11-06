using MediatR;
using Orders.Application.Abstractions;
using Orders.Contracts;
using Orders.Domain;

namespace Orders.Application.Orders;

public sealed record CreateOrderCommand(string CustomerId, IReadOnlyList<CreateOrderItem> Items) : IRequest<Guid>;

public sealed record CreateOrderItem(string Sku, int Quantity, decimal UnitPrice);

public sealed class CreateOrderHandler(
    IOrderRepository orders,
    IOutbox outbox,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<CreateOrderCommand, Guid>
{
    public async Task<Guid> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var lines = request.Items.Select(i => new OrderLine(i.Sku, i.Quantity, i.UnitPrice)).ToList();
        var order = Order.Place(request.CustomerId, lines, clock);

        orders.Add(order);
        outbox.Enqueue(OrderCreated.MessageType, new OrderCreated(
            order.Id,
            order.CustomerId,
            [.. order.Items.Select(i => new OrderCreatedItem(i.Sku, i.Quantity))],
            order.Total,
            order.CreatedAt));

        // One SaveChanges = one transaction: the order and its event are committed together
        // or not at all. The broker is not involved, so it cannot fail this request.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return order.Id;
    }
}
