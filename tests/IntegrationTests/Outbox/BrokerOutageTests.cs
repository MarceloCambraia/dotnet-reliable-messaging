using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using IntegrationTests.Infrastructure;
using Orders.Api.Endpoints;
using Testcontainers.RabbitMq;

namespace IntegrationTests.Outbox;

/// <summary>Required scenario 1: the broker going down must not take order creation down with it.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class BrokerOutageTests(ContainersFixture containers)
{
    [Fact]
    public async Task Orders_are_accepted_while_the_broker_is_down_and_published_once_it_is_back()
    {
        // A dedicated broker we can stop and start. A fixed host port keeps the same address
        // across restarts (Docker would otherwise assign a new random port).
        var port = GetFreeTcpPort();
        await using var broker = new RabbitMqBuilder(ContainersFixture.RabbitMqImage)
            .WithUsername("test")
            .WithPassword("test")
            .WithPortBinding(port, 5672)
            .Build();
        await broker.StartAsync();
        var brokerUri = $"amqp://test:test@{broker.Hostname}:{port}/";

        var exchange = TestNames.Unique("orders");
        await using var api = new OrdersApiFactory(
            containers.NewDatabase("outage"),
            brokerUri,
            new Dictionary<string, string?> { ["Outbox:Exchange"] = exchange });
        using var client = api.CreateClient();

        string captureQueue;
        await using (var probe = await RabbitMqProbe.ConnectAsync(brokerUri))
        {
            captureQueue = await probe.BindCaptureQueueAsync(exchange);
        }

        // Baseline: with the broker up, a new order is published.
        await api.CreateOrderAsync(client);
        await Eventually.GetAsync(() => OutboxStatsAsync(client), s => s.Processed == 1, because: "baseline publish");

        // Broker goes down.
        await broker.StopAsync();

        var response = await client.PostAsJsonAsync("/orders", new CreateOrderRequest("customer-2", [new CreateOrderItemRequest("SKU-2", 1, 5m)]));
        response.StatusCode.Should().Be(HttpStatusCode.Created, "the request never touches the broker");

        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK, "readiness only depends on Postgres");
        using (var health = JsonDocument.Parse(await client.GetStringAsync("/health")))
        {
            health.RootElement.GetProperty("checks").EnumerateArray()
                .Single(c => c.GetProperty("name").GetString() == "rabbitmq")
                .GetProperty("status").GetString().Should().Be("Degraded");
        }

        await Task.Delay(TimeSpan.FromSeconds(2)); // give the publisher several failed polls
        var during = await OutboxStatsAsync(client);
        during.Pending.Should().Be(1, "the event waits in the outbox");
        during.Processed.Should().Be(1);
        during.Failed.Should().Be(0);
        (await api.GetOutboxAsync()).Should().OnlyContain(m => m.Attempts == 0, "an outage must not consume a message's retry budget");

        // Broker comes back: the publisher reconnects and drains the outbox on its own.
        await broker.StartAsync();

        await Eventually.GetAsync(() => OutboxStatsAsync(client), s => s.Processed == 2 && s.Pending == 0, TimeSpan.FromSeconds(60), "drain after recovery");

        var outboxIds = (await api.GetOutboxAsync()).Select(m => m.Id).ToList();
        await using var after = await RabbitMqProbe.ConnectAsync(brokerUri);
        var received = await after.DrainAsync(captureQueue);
        received.Select(m => m.Envelope.MessageId).Should().Contain(outboxIds);
    }

    private static async Task<OutboxStats> OutboxStatsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<OutboxStats>("/diagnostics/outbox"))!;

    private static int GetFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
