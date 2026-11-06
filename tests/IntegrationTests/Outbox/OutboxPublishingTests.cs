using IntegrationTests.Infrastructure;
using Orders.Contracts;
using RabbitMQ.Client;

namespace IntegrationTests.Outbox;

[Collection(IntegrationCollection.Name)]
public sealed class OutboxPublishingTests(ContainersFixture containers)
{
    [Fact]
    public async Task Created_order_is_published_as_a_persistent_envelope_with_tracing_headers()
    {
        var exchange = TestNames.Unique("orders");
        await using var probe = await RabbitMqProbe.ConnectAsync(containers.RabbitMqUri);
        var queue = await probe.BindCaptureQueueAsync(exchange, OrderCreated.MessageType);

        await using var api = new OrdersApiFactory(
            containers.NewDatabase("publishing"),
            containers.RabbitMqUri,
            new Dictionary<string, string?> { ["Outbox:Exchange"] = exchange });
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "corr-e2e");

        var orderId = await api.CreateOrderAsync(client, "SKU-9", 4);

        await Eventually.GetAsync(() => probe.CountAsync(queue), count => count == 1);
        var message = (await probe.DrainAsync(queue)).Single();
        var outbox = (await api.GetOutboxAsync()).Single();

        message.Envelope.MessageId.Should().Be(outbox.Id);
        message.Envelope.Type.Should().Be(OrderCreated.MessageType);
        message.Envelope.CorrelationId.Should().Be("corr-e2e");
        message.Envelope.GetPayload<OrderCreated>().Should().BeEquivalentTo(new
        {
            OrderId = orderId,
            Items = new[] { new OrderCreatedItem("SKU-9", 4) },
        });

        message.Properties.MessageId.Should().Be(outbox.Id.ToString());
        message.Properties.DeliveryMode.Should().Be(DeliveryModes.Persistent);
        message.Properties.Headers.Should().ContainKey("traceparent");
        outbox.ProcessedAt.Should().NotBeNull();
    }
}
