using BuildingBlocks.Messaging.RabbitMq;
using IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Orders.Infrastructure.Outbox;

namespace IntegrationTests.Outbox;

/// <summary>Required scenario 4: competing publisher instances never publish the same message.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class OutboxConcurrencyTests(ContainersFixture containers)
{
    private const int MessageCount = 200;

    [Fact]
    public async Task Two_publishers_draining_the_same_outbox_publish_every_message_exactly_once()
    {
        var publisher = new RecordingPublisher(TimeSpan.FromMilliseconds(5));
        await using var api = new OrdersApiFactory(
            containers.NewDatabase("concurrency"),
            containers.RabbitMqUri,
            new Dictionary<string, string?> { ["Outbox:Enabled"] = "false", ["Outbox:BatchSize"] = "10" },
            services => services.AddSingleton<IMessagePublisher>(publisher));

        await SeedOutboxAsync(api);

        // Two independent processors (own scope, DbContext and connection), started together.
        using var start = new Barrier(2);
        var perInstance = await Task.WhenAll(
            Task.Run(() => DrainAsync(api, start)),
            Task.Run(() => DrainAsync(api, start)));

        publisher.Published.Should().HaveCount(MessageCount);
        publisher.Published.Should().OnlyHaveUniqueItems("SKIP LOCKED gives each instance a disjoint set of rows");
        perInstance.Sum().Should().Be(MessageCount);
        perInstance.Should().OnlyContain(n => n > 0, "both instances must actually have competed for the rows");

        var pending = await api.QueryAsync(db => db.OutboxMessages.CountAsync(m => m.ProcessedAt == null));
        pending.Should().Be(0);
    }

    private static async Task<int> DrainAsync(OrdersApiFactory api, Barrier start)
    {
        start.SignalAndWait();
        var published = 0;
        while (true)
        {
            await using var scope = api.Services.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(CancellationToken.None);
            if (result.Fetched == 0)
            {
                return published; // nothing left that is not locked by the other instance
            }

            published += result.Published;
        }
    }

    private static Task SeedOutboxAsync(OrdersApiFactory api) =>
        api.QueryAsync(async db =>
        {
            var now = DateTimeOffset.UtcNow;
            db.OutboxMessages.AddRange(Enumerable.Range(0, MessageCount).Select(i => new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                Type = "test.seeded.v1",
                Payload = $$"""{"index":{{i}}}""",
                CorrelationId = "seed",
                OccurredAt = now.AddMilliseconds(i),
                NextAttemptAt = now.AddSeconds(-1),
            }));
            return await db.SaveChangesAsync();
        });
}
