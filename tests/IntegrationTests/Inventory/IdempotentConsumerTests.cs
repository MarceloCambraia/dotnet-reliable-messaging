using BuildingBlocks.Messaging;
using Inventory.Worker;
using Inventory.Worker.Domain;
using IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Contracts;

namespace IntegrationTests.Inventory;

/// <summary>Required scenario 2: a message delivered twice has exactly one effect.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class IdempotentConsumerTests(ContainersFixture containers)
{
    [Fact]
    public async Task Same_message_delivered_twice_reserves_stock_once()
    {
        var exchange = TestNames.Unique("orders");
        await using var worker = await InventoryWorkerHost.StartAsync(containers, exchange);
        await using var probe = await RabbitMqProbe.ConnectAsync(containers.RabbitMqUri);

        var duplicate = Envelope(Guid.NewGuid(), new OrderCreatedItem("SKU-1", 3));
        var sentinel = Envelope(Guid.NewGuid(), new OrderCreatedItem("SKU-2", 1));

        await probe.PublishAsync(exchange, OrderCreated.MessageType, duplicate);
        await probe.PublishAsync(exchange, OrderCreated.MessageType, duplicate);
        // The consumer processes one message at a time, in order: once the sentinel is
        // reserved, both copies of the duplicate have been handled.
        await probe.PublishAsync(exchange, OrderCreated.MessageType, sentinel);

        var sentinelOrderId = sentinel.GetPayload<OrderCreated>().OrderId;
        await Eventually.GetAsync(
            () => worker.QueryAsync(db => db.StockReservations.AnyAsync(r => r.OrderId == sentinelOrderId)),
            done => done);

        var duplicateOrderId = duplicate.GetPayload<OrderCreated>().OrderId;
        var state = await worker.QueryAsync(async db => new
        {
            Reservations = await db.StockReservations.CountAsync(r => r.OrderId == duplicateOrderId),
            Available = (await db.Products.SingleAsync(p => p.Sku == "SKU-1")).AvailableQuantity,
            Inbox = await db.InboxMessages.CountAsync(),
        });

        state.Reservations.Should().Be(1);
        state.Available.Should().Be(97, "100 seeded minus a single reservation of 3");
        state.Inbox.Should().Be(2, "one row per distinct MessageId: the duplicate and the sentinel");
    }

    [Fact]
    public async Task Concurrent_duplicates_are_serialized_by_the_inbox_primary_key()
    {
        await using var worker = await InventoryWorkerHost.StartAsync(containers, TestNames.Unique("orders"));
        var envelope = Envelope(Guid.NewGuid(), new OrderCreatedItem("SKU-3", 5));

        // Simulates two consumer instances receiving the same redelivered message at once.
        await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            await using var scope = worker.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<OrderCreatedHandler>().HandleAsync(envelope, CancellationToken.None);
        }));

        var state = await worker.QueryAsync(async db => new
        {
            Reservations = await db.StockReservations.CountAsync(),
            Available = (await db.Products.SingleAsync(p => p.Sku == "SKU-3")).AvailableQuantity,
        });
        state.Reservations.Should().Be(1);
        state.Available.Should().Be(45);
    }

    [Fact]
    public async Task Insufficient_stock_is_a_business_outcome_not_a_failure()
    {
        var exchange = TestNames.Unique("orders");
        await using var worker = await InventoryWorkerHost.StartAsync(containers, exchange);
        await using var probe = await RabbitMqProbe.ConnectAsync(containers.RabbitMqUri);
        var envelope = Envelope(Guid.NewGuid(), new OrderCreatedItem("SKU-5", 1)); // seeded with 0 units

        await probe.PublishAsync(exchange, OrderCreated.MessageType, envelope);

        var orderId = envelope.GetPayload<OrderCreated>().OrderId;
        var reservation = await Eventually.GetAsync(
            () => worker.QueryAsync(db => db.StockReservations.AsNoTracking().FirstOrDefaultAsync(r => r.OrderId == orderId)),
            r => r is not null);

        reservation!.Status.Should().Be(ReservationStatus.Rejected);
        reservation.RejectionReason.Should().Contain("Insufficient stock for SKU-5");
        (await probe.CountAsync($"{worker.Queue}.dlq")).Should().Be(0u);
    }

    private static MessageEnvelope Envelope(Guid orderId, params OrderCreatedItem[] items) =>
        MessageEnvelope.Create(
            Guid.NewGuid(),
            OrderCreated.MessageType,
            "corr-test",
            DateTimeOffset.UtcNow,
            new OrderCreated(orderId, "customer-1", items, 10m, DateTimeOffset.UtcNow));
}
