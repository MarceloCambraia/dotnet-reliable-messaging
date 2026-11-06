using BuildingBlocks.Messaging.RabbitMq;
using IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Orders.Infrastructure.Outbox;
using RabbitMQ.Client.Exceptions;

namespace IntegrationTests.Outbox;

[Collection(IntegrationCollection.Name)]
public sealed class OutboxRetryTests(ContainersFixture containers)
{
    [Fact]
    public async Task Message_level_failures_are_retried_and_parked_after_max_attempts()
    {
        var publisher = new FailingPublisher(() => new InvalidOperationException("routing rejected"));
        await using var api = CreateApi(publisher, maxAttempts: 3);
        using var client = api.CreateClient();
        await api.CreateOrderAsync(client);

        for (var i = 0; i < 3; i++)
        {
            var result = await ProcessBatchAsync(api);
            result.Failed.Should().Be(1);
        }

        var message = (await api.GetOutboxAsync()).Single();
        message.Attempts.Should().Be(3);
        message.FailedAt.Should().NotBeNull();
        message.ProcessedAt.Should().BeNull();
        message.LastError.Should().Be("InvalidOperationException: routing rejected");

        (await ProcessBatchAsync(api)).Fetched.Should().Be(0, "parked messages are no longer picked up");
    }

    [Fact]
    public async Task Failed_attempt_schedules_the_next_one_with_backoff()
    {
        var publisher = new FailingPublisher(() => new InvalidOperationException("boom"));
        await using var api = CreateApi(publisher, maxAttempts: 5, baseRetryDelay: "00:01:00");
        using var client = api.CreateClient();
        await api.CreateOrderAsync(client);

        await ProcessBatchAsync(api);
        var message = (await api.GetOutboxAsync()).Single();

        message.Attempts.Should().Be(1);
        message.NextAttemptAt.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(30));
        (await ProcessBatchAsync(api)).Fetched.Should().Be(0, "the message is not due yet");
    }

    [Fact]
    public async Task Broker_outage_stops_the_batch_without_consuming_attempts()
    {
        var publisher = new FailingPublisher(() => new BrokerUnreachableException(new IOException("connection refused")));
        await using var api = CreateApi(publisher, maxAttempts: 1);
        using var client = api.CreateClient();
        await api.CreateOrderAsync(client);
        await api.CreateOrderAsync(client);

        var result = await ProcessBatchAsync(api);

        result.BrokerUnavailable.Should().BeTrue();
        publisher.Calls.Should().Be(1, "the batch stops at the first connectivity failure");
        (await api.GetOutboxAsync()).Should().OnlyContain(m => m.Attempts == 0 && m.FailedAt == null && m.ProcessedAt == null);
    }

    private OrdersApiFactory CreateApi(FailingPublisher publisher, int maxAttempts, string baseRetryDelay = "00:00:00") =>
        new(
            containers.NewDatabase("retry"),
            containers.RabbitMqUri,
            new Dictionary<string, string?>
            {
                ["Outbox:Enabled"] = "false",
                ["Outbox:MaxAttempts"] = maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Outbox:BaseRetryDelay"] = baseRetryDelay,
            },
            services => services.AddSingleton<IMessagePublisher>(publisher));

    private static async Task<OutboxBatchResult> ProcessBatchAsync(OrdersApiFactory api)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
    }
}
