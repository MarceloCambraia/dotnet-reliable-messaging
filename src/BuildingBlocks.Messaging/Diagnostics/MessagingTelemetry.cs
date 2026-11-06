using System.Diagnostics;

namespace BuildingBlocks.Messaging.Diagnostics;

/// <summary>
/// Creates producer/consumer spans and carries W3C trace context through AMQP headers,
/// so a single trace spans API → outbox → broker → consumer.
/// </summary>
public static class MessagingTelemetry
{
    public const string ActivitySourceName = "BuildingBlocks.Messaging";

    public static ActivitySource Source { get; } = new(ActivitySourceName);

    public static Activity? StartPublish(string exchange, string routingKey, MessageEnvelope envelope, string? parentTraceParent)
    {
        ActivityContext.TryParse(parentTraceParent, traceState: null, isRemote: true, out var parent);

        var activity = Source.StartActivity($"{exchange} publish", ActivityKind.Producer, parent);
        if (activity is not null)
        {
            SetCommonTags(activity, envelope, exchange, "publish");
            activity.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        }

        return activity;
    }

    public static Activity? StartProcess(string queue, MessageEnvelope? envelope, IDictionary<string, object?>? headers)
    {
        var parent = ExtractContext(headers);
        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, parent);
        if (activity is not null)
        {
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.operation.type", "process");
            activity.SetTag("messaging.destination.name", queue);
            if (envelope is not null)
            {
                SetCommonTags(activity, envelope, queue, "process");
            }
        }

        return activity;
    }

    /// <summary>Writes <c>traceparent</c>/<c>tracestate</c> for <paramref name="activity"/> into the headers.</summary>
    public static void Inject(Activity? activity, IDictionary<string, object?> headers)
    {
        if (activity is null)
        {
            return;
        }

        DistributedContextPropagator.Current.Inject(activity, headers, static (carrier, key, value) =>
        {
            if (carrier is IDictionary<string, object?> dictionary)
            {
                dictionary[key] = value;
            }
        });
    }

    public static ActivityContext ExtractContext(IDictionary<string, object?>? headers)
    {
        if (headers is null)
        {
            return default;
        }

        DistributedContextPropagator.Current.ExtractTraceIdAndState(
            headers,
            static (object? carrier, string fieldName, out string? fieldValue, out IEnumerable<string>? fieldValues) =>
            {
                fieldValues = null;
                fieldValue = MessageHeaders.GetString(carrier as IDictionary<string, object?>, fieldName);
            },
            out var traceParent,
            out var traceState);

        return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out var context) ? context : default;
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.AddException(exception);
    }

    private static void SetCommonTags(Activity activity, MessageEnvelope envelope, string destination, string operation)
    {
        activity.SetTag("messaging.system", "rabbitmq");
        activity.SetTag("messaging.operation.type", operation);
        activity.SetTag("messaging.destination.name", destination);
        activity.SetTag("messaging.message.id", envelope.MessageId.ToString());
        activity.SetTag("messaging.message.conversation_id", envelope.CorrelationId);
        activity.SetTag("messaging.message.type", envelope.Type);
    }
}
