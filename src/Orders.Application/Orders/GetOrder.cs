using MediatR;
using Orders.Application.Abstractions;

namespace Orders.Application.Orders;

public sealed record GetOrderQuery(Guid OrderId) : IRequest<OrderDto?>;

public sealed record OrderDto(
    Guid Id,
    string CustomerId,
    string Status,
    decimal Total,
    DateTimeOffset CreatedAt,
    IReadOnlyList<OrderItemDto> Items);

public sealed record OrderItemDto(string Sku, int Quantity, decimal UnitPrice);

public sealed class GetOrderHandler(IOrderRepository orders) : IRequestHandler<GetOrderQuery, OrderDto?>
{
    public async Task<OrderDto?> Handle(GetOrderQuery request, CancellationToken cancellationToken)
    {
        var order = await orders.FindAsync(request.OrderId, cancellationToken);

        return order is null
            ? null
            : new OrderDto(
                order.Id,
                order.CustomerId,
                order.Status.ToString(),
                order.Total,
                order.CreatedAt,
                [.. order.Items.Select(i => new OrderItemDto(i.Sku, i.Quantity, i.UnitPrice))]);
    }
}
