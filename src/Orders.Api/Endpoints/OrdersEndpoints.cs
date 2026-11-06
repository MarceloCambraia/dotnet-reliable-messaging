using MediatR;
using Orders.Application.Orders;

namespace Orders.Api.Endpoints;

public sealed record CreateOrderRequest(string CustomerId, IReadOnlyList<CreateOrderItemRequest>? Items);

public sealed record CreateOrderItemRequest(string Sku, int Quantity, decimal UnitPrice);

public sealed record CreateOrderResponse(Guid OrderId);

internal static class OrdersEndpoints
{
    public static IEndpointRouteBuilder MapOrdersEndpoints(this IEndpointRouteBuilder app)
    {
        var orders = app.MapGroup("/orders").WithTags("Orders");

        orders.MapPost("/", async (CreateOrderRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new CreateOrderCommand(
                    request.CustomerId,
                    [.. (request.Items ?? []).Select(i => new CreateOrderItem(i.Sku, i.Quantity, i.UnitPrice))]);

                var orderId = await sender.Send(command, cancellationToken);
                return Results.Created($"/orders/{orderId}", new CreateOrderResponse(orderId));
            })
            .WithName("CreateOrder")
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        orders.MapGet("/{orderId:guid}", async (Guid orderId, ISender sender, CancellationToken cancellationToken) =>
                await sender.Send(new GetOrderQuery(orderId), cancellationToken) is { } order
                    ? Results.Ok(order)
                    : Results.NotFound())
            .WithName("GetOrder")
            .Produces<OrderDto>()
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }
}
