using System.Text;
using BuildingBlocks.Messaging;
using IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Orders.Contracts;

namespace IntegrationTests.Inventory;

/// <summary>Required scenario 3: a message that keeps failing ends up in the DLQ after N retries.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class DeadLetterTests(ContainersFixture containers)
{
    private const string FailingSku = "SKU-FAIL";

    [Fact]
    public async Task Message_that_keeps_failing_is_retried_then_dead_lettered()
    {
        var exchange = TestNames.Unique("orders");
        await using var worker = await InventoryWorkerHost.StartAsync(containers, exchange, failOnSku: FailingSku);
        await using var probe = await RabbitMqProbe.ConnectAsync(containers.RabbitMqUri);
        var envelope = MessageEnvelope.Create(
            Guid.NewGuid(),
            OrderCreated.MessageType,
            "corr-dlq",
            DateTimeOffset.UtcNow,
            new OrderCreated(Guid.NewGuid(), "c", [new OrderCreatedItem(FailingSku, 1)], 1m, DateTimeOffset.UtcNow));

        await probe.PublishAsync(exchange, OrderCreated.MessageType, envelope);

        var deadLetterQueue = $"{worker.Queue}.dlq";
        await Eventually.GetAsync(() => probe.CountAsync(deadLetterQueue), count => count == 1, because: "message in DLQ");

        var dead = (await probe.DrainAsync(deadLetterQueue)).Single();
        dead.Envelope.MessageId.Should().Be(envelope.MessageId, "the original message is preserved for inspection and replay");
        MessageHeaders.GetInt32(dead.Properties.Headers, MessageHeaders.RetryCount).Should().Be(3, "1 delivery + 3 retries");
        MessageHeaders.GetString(dead.Properties.Headers, MessageHeaders.ExceptionType).Should().EndWith("SimulatedFailureException");
        MessageHeaders.GetString(dead.Properties.Headers, MessageHeaders.FailedQueue).Should().Be(worker.Queue);

        for (var retry = 1; retry <= 3; retry++)
        {
            (await probe.CountAsync($"{worker.Queue}.retry.{retry}")).Should().Be(0u);
        }

        (await worker.QueryAsync(db => db.InboxMessages.CountAsync())).Should().Be(0, "a failed attempt commits nothing");
    }

    [Fact]
    public async Task Poison_message_skips_retries_and_is_dead_lettered_by_the_broker()
    {
        var exchange = TestNames.Unique("orders");
        await using var worker = await InventoryWorkerHost.StartAsync(containers, exchange);
        await using var probe = await RabbitMqProbe.ConnectAsync(containers.RabbitMqUri);

        await probe.PublishAsync(exchange, OrderCreated.MessageType, Encoding.UTF8.GetBytes("this is not an envelope"), messageId: "poison-1");

        var deadLetterQueue = $"{worker.Queue}.dlq";
        await Eventually.GetAsync(() => probe.CountAsync(deadLetterQueue), count => count == 1);

        var (properties, body) = (await probe.DrainRawAsync(deadLetterQueue)).Single();
        Encoding.UTF8.GetString(body).Should().Be("this is not an envelope");
        properties.MessageId.Should().Be("poison-1");
        properties.Headers.Should().NotBeNull();
        properties.Headers!.Keys.Should().Contain(
            k => k == "x-death" || k.StartsWith("x-first-death", StringComparison.Ordinal),
            "the broker dead-lettered it via the queue's DLX and recorded why");
        MessageHeaders.GetInt32(properties.Headers, MessageHeaders.RetryCount).Should().Be(0, "poison messages are never retried");
    }
}
