using System.Diagnostics;
using System.Text;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.Diagnostics;

namespace UnitTests.Messaging;

public sealed class MessagingTelemetryTests : IDisposable
{
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = source => source.Name == MessagingTelemetry.ActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public MessagingTelemetryTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Publish_span_continues_the_trace_stored_with_the_outbox_message()
    {
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var traceParent = $"00-{parent.TraceId}-{parent.SpanId}-01";

        using var activity = MessagingTelemetry.StartPublish("ex", "rk", Envelope(), traceParent);

        activity.Should().NotBeNull();
        activity!.Kind.Should().Be(ActivityKind.Producer);
        activity.TraceId.Should().Be(parent.TraceId);
        activity.ParentSpanId.Should().Be(parent.SpanId);
    }

    [Fact]
    public void Consumer_span_is_a_child_of_the_producer_span_carried_in_headers()
    {
        var headers = new Dictionary<string, object?>();
        using (var producer = MessagingTelemetry.StartPublish("ex", "rk", Envelope(), parentTraceParent: null))
        {
            MessagingTelemetry.Inject(producer, headers);

            // On the consumer side the broker hands header strings back as byte arrays.
            var received = headers.ToDictionary(h => h.Key, h => (object?)Encoding.UTF8.GetBytes((string)h.Value!));

            using var consumer = MessagingTelemetry.StartProcess("queue", Envelope(), received);

            consumer!.Kind.Should().Be(ActivityKind.Consumer);
            consumer.TraceId.Should().Be(producer!.TraceId);
            consumer.ParentSpanId.Should().Be(producer.SpanId);
        }
    }

    [Fact]
    public void Missing_or_invalid_context_starts_a_new_trace()
    {
        MessagingTelemetry.ExtractContext(null).Should().Be(default(ActivityContext));
        MessagingTelemetry.ExtractContext(new Dictionary<string, object?> { ["traceparent"] = "garbage" })
            .Should().Be(default(ActivityContext));
    }

    private static MessageEnvelope Envelope() =>
        MessageEnvelope.Create(Guid.NewGuid(), "t", "c", DateTimeOffset.UnixEpoch, new { });
}
