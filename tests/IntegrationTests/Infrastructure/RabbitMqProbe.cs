using BuildingBlocks.Messaging;
using RabbitMQ.Client;

namespace IntegrationTests.Infrastructure;

public sealed record ReceivedMessage(IReadOnlyBasicProperties Properties, MessageEnvelope Envelope);

/// <summary>Test-side RabbitMQ client used to observe and inject messages.</summary>
public sealed class RabbitMqProbe : IAsyncDisposable
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;

    private RabbitMqProbe(IConnection connection, IChannel channel)
    {
        _connection = connection;
        _channel = channel;
    }

    public static async Task<RabbitMqProbe> ConnectAsync(string uri)
    {
        var connection = await new ConnectionFactory { Uri = new Uri(uri), ClientProvidedName = "integration-tests" }.CreateConnectionAsync();
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true));
        return new RabbitMqProbe(connection, channel);
    }

    /// <summary>Declares a durable queue receiving everything published to <paramref name="exchange"/>.</summary>
    public async Task<string> BindCaptureQueueAsync(string exchange, string routingKey = "#")
    {
        var queue = TestNames.Unique("capture");
        await _channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        await _channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false);
        await _channel.QueueBindAsync(queue, exchange, routingKey);
        return queue;
    }

    public async Task<IReadOnlyList<ReceivedMessage>> DrainAsync(string queue)
    {
        var messages = new List<ReceivedMessage>();
        while (await _channel.BasicGetAsync(queue, autoAck: true) is { } result)
        {
            messages.Add(new ReceivedMessage(result.BasicProperties, EnvelopeSerializer.Deserialize(result.Body.Span)));
        }

        return messages;
    }

    public async Task<IReadOnlyList<(IReadOnlyBasicProperties Properties, byte[] Body)>> DrainRawAsync(string queue)
    {
        var messages = new List<(IReadOnlyBasicProperties, byte[])>();
        while (await _channel.BasicGetAsync(queue, autoAck: true) is { } result)
        {
            messages.Add((result.BasicProperties, result.Body.ToArray()));
        }

        return messages;
    }

    public async Task<uint> CountAsync(string queue) => await _channel.MessageCountAsync(queue);

    /// <summary>Consumers attached to <paramref name="queue"/>; 0 if the queue does not exist yet.</summary>
    public async Task<uint> ConsumerCountAsync(string queue)
    {
        // A passive declare of a missing queue closes the channel (404), so use a throwaway one.
        await using var channel = await _connection.CreateChannelAsync();
        try
        {
            return (await channel.QueueDeclarePassiveAsync(queue)).ConsumerCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return 0;
        }
    }

    public async Task PublishAsync(string exchange, string routingKey, ReadOnlyMemory<byte> body, string? messageId = null)
    {
        var properties = new BasicProperties { MessageId = messageId, DeliveryMode = DeliveryModes.Persistent, ContentType = "application/json" };
        await _channel.BasicPublishAsync(exchange, routingKey, mandatory: true, basicProperties: properties, body: body);
    }

    public Task PublishAsync(string exchange, string routingKey, MessageEnvelope envelope) =>
        PublishAsync(exchange, routingKey, EnvelopeSerializer.Serialize(envelope), envelope.MessageId.ToString());

    public async ValueTask DisposeAsync()
    {
        await _channel.DisposeAsync();
        await _connection.DisposeAsync();
    }
}
