using System.Collections.Concurrent;
using System.Diagnostics;
using BuildingBlocks.Messaging.Diagnostics;
using IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace IntegrationTests.EndToEnd;

/// <summary>One trace must span the HTTP request, the (deferred) publish and the consumer.</summary>
[Collection(IntegrationCollection.Name)]
public sealed class TracePropagationTests(ContainersFixture containers) : IDisposable
{
    private readonly ConcurrentBag<Activity> _activities = [];
    private ActivityListener? _listener;

    public void Dispose() => _listener?.Dispose();

    [Fact]
    public async Task Request_publish_and_consume_spans_share_one_trace()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is MessagingTelemetry.ActivitySourceName or "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _activities.Add,
        };
        ActivitySource.AddActivityListener(_listener);

        var exchange = TestNames.Unique("orders");
        await using var worker = await InventoryWorkerHost.StartAsync(containers, exchange);
        await using var api = new OrdersApiFactory(
            containers.NewDatabase("tracing"),
            containers.RabbitMqUri,
            new Dictionary<string, string?> { ["Outbox:Exchange"] = exchange });
        using var client = api.CreateClient();

        var orderId = await api.CreateOrderAsync(client);
        await Eventually.GetAsync(
            () => worker.QueryAsync(db => db.StockReservations.AnyAsync(r => r.OrderId == orderId)),
            done => done);

        var messageId = (await api.GetOutboxAsync()).Single().Id.ToString();
        var publish = await SpanAsync(ActivityKind.Producer, messageId);
        var process = await SpanAsync(ActivityKind.Consumer, messageId);
        var request = _activities.Single(a => a.Kind == ActivityKind.Server && a.TraceId == publish.TraceId);

        request.DisplayName.Should().Contain("POST");
        publish.TraceId.Should().Be(request.TraceId, "the publish happens later, in the background, but continues the request's trace");
        process.TraceId.Should().Be(publish.TraceId);
        process.ParentSpanId.Should().Be(publish.SpanId, "context travelled in the AMQP headers");
    }

    private async Task<Activity> SpanAsync(ActivityKind kind, string messageId) =>
        (await Eventually.GetAsync(
            () => Task.FromResult(_activities.FirstOrDefault(a =>
                a.Kind == kind && (string?)a.GetTagItem("messaging.message.id") == messageId)),
            a => a is not null))!;
}
