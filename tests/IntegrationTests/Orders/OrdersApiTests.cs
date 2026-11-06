using System.Net;
using System.Net.Http.Json;
using IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Orders.Api.Endpoints;
using Orders.Application.Orders;
using Orders.Contracts;

namespace IntegrationTests.Orders;

[Collection(IntegrationCollection.Name)]
public sealed class OrdersApiTests(ContainersFixture containers) : IAsyncLifetime
{
    private OrdersApiFactory _api = null!;

    public Task InitializeAsync()
    {
        // Publisher off: these tests look at what the request itself commits.
        _api = new OrdersApiFactory(
            containers.NewDatabase("orders"),
            containers.RabbitMqUri,
            new Dictionary<string, string?> { ["Outbox:Enabled"] = "false" });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Creating_an_order_commits_the_order_and_its_outbox_message_together()
    {
        using var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-123");

        var response = await client.PostAsJsonAsync("/orders", ValidOrder());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateOrderResponse>();
        response.Headers.Location!.ToString().Should().Be($"/orders/{created!.OrderId}");

        var (orders, outbox) = await _api.QueryAsync(async db =>
            (await db.Orders.CountAsync(), await db.OutboxMessages.AsNoTracking().ToListAsync()));

        orders.Should().Be(1);
        var message = outbox.Should().ContainSingle().Subject;
        message.Type.Should().Be(OrderCreated.MessageType);
        message.CorrelationId.Should().Be("corr-123");
        message.ProcessedAt.Should().BeNull("the publisher is disabled, so it is still pending");
        message.Payload.Should().Contain(created.OrderId.ToString());
    }

    [Fact]
    public async Task Rejected_order_returns_problem_details_and_writes_nothing()
    {
        using var client = _api.CreateClient();

        var response = await client.PostAsJsonAsync("/orders", new CreateOrderRequest("customer-1", []));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var counts = await _api.QueryAsync(async db => (await db.Orders.CountAsync(), await db.OutboxMessages.CountAsync()));
        counts.Should().Be((0, 0));
    }

    [Fact]
    public async Task Created_order_can_be_read_back()
    {
        using var client = _api.CreateClient();
        var created = await (await client.PostAsJsonAsync("/orders", ValidOrder())).Content.ReadFromJsonAsync<CreateOrderResponse>();

        var order = await client.GetFromJsonAsync<OrderDto>($"/orders/{created!.OrderId}");

        order!.CustomerId.Should().Be("customer-1");
        order.Total.Should().Be(25m);
        order.Items.Should().ContainSingle(i => i.Sku == "SKU-1" && i.Quantity == 2);
    }

    [Fact]
    public async Task Unknown_order_returns_404()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/orders/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private static CreateOrderRequest ValidOrder() =>
        new("customer-1", [new CreateOrderItemRequest("SKU-1", 2, 12.5m)]);
}
