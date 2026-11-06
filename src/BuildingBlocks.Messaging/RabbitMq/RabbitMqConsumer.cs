using BuildingBlocks.Messaging.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging.RabbitMq;

/// <summary>
/// Long-running consumer with manual acks. It owns the reconnect loop: when the connection or
/// channel drops, it backs off, reconnects, redeclares the topology and resumes consuming.
/// Unacked in-flight messages are redelivered by the broker, which is why handlers must be idempotent.
/// </summary>
public sealed partial class RabbitMqConsumer<THandler>(
    IRabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    ConsumerQueue queue,
    ushort prefetchCount,
    ILogger<RabbitMqConsumer<THandler>> logger) : BackgroundService
    where THandler : IMessageHandler
{
    private const int MaxHeaderLength = 1_000;
    private static readonly TimeSpan MaxReconnectDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilDisconnectedAsync(() => delay = TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The reconnect loop must survive any failure.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogConsumerFailed(ex, queue.Name, delay);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
        }
    }

    private async Task ConsumeUntilDisconnectedAsync(Action onConnected, CancellationToken stoppingToken)
    {
        var current = await connection.GetAsync(stoppingToken);

        await using var channel = await current.CreateChannelAsync(cancellationToken: stoppingToken);
        await using var republishChannel = await current.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);

        await queue.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: prefetchCount, global: false, cancellationToken: stoppingToken);

        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        channel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };
        republishChannel.ChannelShutdownAsync += (_, _) =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, republishChannel, delivery, stoppingToken);

        await channel.BasicConsumeAsync(queue.Name, autoAck: false, consumer, stoppingToken);
        onConnected();
        LogConsuming(queue.Name, prefetchCount);

        await closed.Task.WaitAsync(stoppingToken);
        LogChannelClosed(queue.Name);
    }

    private async Task OnMessageAsync(IChannel channel, IChannel republishChannel, BasicDeliverEventArgs delivery, CancellationToken stoppingToken)
    {
        MessageEnvelope envelope;
        try
        {
            envelope = EnvelopeSerializer.Deserialize(delivery.Body.Span);
        }
        catch (InvalidMessageException ex)
        {
            // Poison message: no amount of retrying will help. Rejecting without requeue makes the
            // broker dead-letter it through the queue's x-dead-letter-exchange.
            LogPoisonMessage(ex, queue.Name, delivery.BasicProperties.MessageId);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
            return;
        }

        var retries = MessageHeaders.GetInt32(delivery.BasicProperties.Headers, MessageHeaders.RetryCount);
        using var activity = MessagingTelemetry.StartProcess(queue.Name, envelope, delivery.BasicProperties.Headers);
        activity?.SetTag("messaging.rabbitmq.retry_count", retries);
        using var logScope = logger.BeginScope(new Dictionary<string, object>
        {
            ["MessageId"] = envelope.MessageId,
            ["CorrelationId"] = envelope.CorrelationId,
            ["MessageType"] = envelope.Type,
        });

        try
        {
            await using (var scope = scopeFactory.CreateAsyncScope())
            {
                var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                await handler.HandleAsync(envelope, stoppingToken);
            }

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: leave the message unacked so the broker redelivers it to someone else.
        }
#pragma warning disable CA1031 // Any handler failure is routed to retry / DLQ.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MessagingTelemetry.RecordException(activity, ex);
            await HandleFailureAsync(channel, republishChannel, delivery, retries, ex);
        }
    }

    private async Task HandleFailureAsync(IChannel channel, IChannel republishChannel, BasicDeliverEventArgs delivery, int retries, Exception error)
    {
        var properties = new BasicProperties(delivery.BasicProperties)
        {
            Headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>(), StringComparer.Ordinal),
        };

        string exchange, routingKey;
        if (error is not InvalidMessageException && retries < queue.MaxRetries)
        {
            properties.Headers[MessageHeaders.RetryCount] = retries + 1;
            exchange = string.Empty; // default exchange routes by queue name
            routingKey = queue.RetryQueue(retries + 1);
            LogRetryScheduled(error, retries + 1, queue.MaxRetries, queue.RetryDelays[retries]);
        }
        else
        {
            properties.Headers[MessageHeaders.ExceptionType] = error.GetType().FullName;
            properties.Headers[MessageHeaders.ExceptionMessage] = Truncate(error.Message);
            properties.Headers[MessageHeaders.FailedQueue] = queue.Name;
            exchange = queue.DeadLetterExchange;
            routingKey = queue.Name;
            LogDeadLettered(error, retries + 1, queue.DeadLetterQueue);
        }

        try
        {
            // Publish-then-ack: if we crash in between, the message is processed again (at-least-once),
            // never lost. The idempotent handler absorbs the duplicate.
            await republishChannel.BasicPublishAsync(exchange, routingKey, mandatory: true, basicProperties: properties, body: delivery.Body, CancellationToken.None);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, CancellationToken.None);
        }
#pragma warning disable CA1031
        catch (Exception republishError)
#pragma warning restore CA1031
        {
            LogRepublishFailed(republishError);
            if (channel.IsOpen)
            {
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
            }
        }
    }

    private static string Truncate(string value) => value.Length <= MaxHeaderLength ? value : value[..MaxHeaderLength];

    [LoggerMessage(Level = LogLevel.Information, Message = "Consuming from {Queue} (prefetch {Prefetch})")]
    private partial void LogConsuming(string queue, ushort prefetch);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Channel for {Queue} closed; reconnecting")]
    private partial void LogChannelClosed(string queue);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consumer for {Queue} failed; reconnecting in {Delay}")]
    private partial void LogConsumerFailed(Exception exception, string queue, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Poison message {MessageId} rejected from {Queue}")]
    private partial void LogPoisonMessage(Exception exception, string queue, string? messageId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Handler failed; scheduling retry {Retry}/{MaxRetries} in {Delay}")]
    private partial void LogRetryScheduled(Exception exception, int retry, int maxRetries, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Handler failed after {Attempts} attempts; moved to {DeadLetterQueue}")]
    private partial void LogDeadLettered(Exception exception, int attempts, string deadLetterQueue);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not republish failed message; requeueing it")]
    private partial void LogRepublishFailed(Exception exception);
}
