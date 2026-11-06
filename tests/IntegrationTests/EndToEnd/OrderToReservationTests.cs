using Inventory.Worker.Domain;
using IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace IntegrationTests.EndToEnd;

/// <summary>Happy path through every component: API → outbox → RabbitMQ → worker → inbox.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class OrderToReservationTests(ContainersFixture containers)
{
    [Fact]
    public async Task Placing_an_order_reserves_its_stock_in_the_inventory_service()
    {
        var exchange = TestNames.Unique("orders");
        await using var worker = await InventoryWorkerHost.StartAsync(containers, exchange);
        await using var api = new OrdersApiFactory(
            containers.NewDatabase("orders"),
            containers.RabbitMqUri,
            new Dictionary<string, string?> { ["Outbox:Exchange"] = exchange });
        using var client = api.CreateClient();

        var orderId = await api.CreateOrderAsync(client, "SKU-4", 2);

        var reservation = await Eventually.GetAsync(
            () => worker.QueryAsync(db => db.StockReservations.AsNoTracking().FirstOrDefaultAsync(r => r.OrderId == orderId)),
            r => r is not null);
        reservation!.Status.Should().Be(ReservationStatus.Reserved);

        var outboxMessageId = (await api.GetOutboxAsync()).Single().Id;
        var inbox = await worker.QueryAsync(db => db.InboxMessages.AsNoTracking().SingleAsync());
        inbox.MessageId.Should().Be(outboxMessageId, "the outbox id travels end to end as the idempotency key");
        (await worker.QueryAsync(db => db.Products.SingleAsync(p => p.Sku == "SKU-4"))).AvailableQuantity.Should().Be(8);
    }
}
