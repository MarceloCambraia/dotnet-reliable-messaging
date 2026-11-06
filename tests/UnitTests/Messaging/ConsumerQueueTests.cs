using BuildingBlocks.Messaging.RabbitMq;

namespace UnitTests.Messaging;

public sealed class ConsumerQueueTests
{
    [Fact]
    public void Derives_retry_and_dead_letter_names_from_the_queue_name()
    {
        var queue = new ConsumerQueue("inventory.order-created", "orders.events", ["orders.#"], [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)]);

        queue.MaxRetries.Should().Be(2);
        queue.RetryQueue(1).Should().Be("inventory.order-created.retry.1");
        queue.RetryQueue(2).Should().Be("inventory.order-created.retry.2");
        queue.DeadLetterExchange.Should().Be("inventory.order-created.dlx");
        queue.DeadLetterQueue.Should().Be("inventory.order-created.dlq");
    }
}
