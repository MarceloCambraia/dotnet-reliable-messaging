using BuildingBlocks.Messaging.Diagnostics;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging.RabbitMq;

public interface IMessagePublisher
{
    /// <summary>
    /// Publishes and waits for the broker's publisher confirm. Returning normally means the
    /// broker has taken responsibility for the message (persisted to a durable queue).
    /// </summary>
    /// <exception cref="RabbitMQ.Client.Exceptions.PublishException">Broker nacked or returned (unroutable) the message.</exception>
    /// <exception cref="TimeoutException">No confirm within <see cref="RabbitMqOptions.OperationTimeout"/>.</exception>
    Task PublishAsync(
        string exchange,
        string routingKey,
        MessageEnvelope envelope,
        string? parentTraceParent,
        CancellationToken cancellationToken);
}

public sealed class RabbitMqPublisher(
    IRabbitMqConnection connection,
    IOptions<RabbitMqOptions> options) : IMessagePublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HashSet<string> _declaredExchanges = new(StringComparer.Ordinal);
    private IChannel? _channel;

    public async Task PublishAsync(
        string exchange,
        string routingKey,
        MessageEnvelope envelope,
        string? parentTraceParent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using var activity = MessagingTelemetry.StartPublish(exchange, routingKey, envelope, parentTraceParent);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.Value.OperationTimeout);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(exchange, timeout.Token);

            var headers = new Dictionary<string, object?>(StringComparer.Ordinal);
            MessagingTelemetry.Inject(activity, headers);

            var properties = new BasicProperties
            {
                MessageId = envelope.MessageId.ToString(),
                CorrelationId = envelope.CorrelationId,
                Type = envelope.Type,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(envelope.OccurredAt.ToUnixTimeSeconds()),
                Headers = headers,
            };

            // mandatory: an unroutable message comes back as a PublishException instead of
            // being silently dropped by the broker.
            await channel.BasicPublishAsync(
                exchange,
                routingKey,
                mandatory: true,
                basicProperties: properties,
                body: EnvelopeSerializer.Serialize(envelope),
                cancellationToken: timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var error = new TimeoutException($"No publisher confirm for message {envelope.MessageId} within {options.Value.OperationTimeout}.");
            MessagingTelemetry.RecordException(activity, error);
            throw error;
        }
        catch (Exception ex)
        {
            MessagingTelemetry.RecordException(activity, ex);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }

    private async Task<IChannel> GetChannelAsync(string exchange, CancellationToken cancellationToken)
    {
        if (_channel?.IsOpen != true)
        {
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
                _channel = null;
            }

            _declaredExchanges.Clear();
            var current = await connection.GetAsync(cancellationToken);

            // With confirmation tracking enabled, BasicPublishAsync only completes once the
            // broker has confirmed (ack) the message, and throws on nack/return.
            _channel = await current.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                cancellationToken);
        }

        if (_declaredExchanges.Add(exchange))
        {
            await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        }

        return _channel;
    }
}
