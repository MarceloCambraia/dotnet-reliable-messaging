using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging.RabbitMq;

public interface IRabbitMqConnection
{
    bool IsConnected { get; }

    /// <summary>Returns an open connection, (re)connecting if needed.</summary>
    /// <exception cref="RabbitMQ.Client.Exceptions.BrokerUnreachableException">The broker is down.</exception>
    Task<IConnection> GetAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One long-lived connection per process (connections are expensive, channels are cheap).
/// The client library's automatic recovery is disabled on purpose: callers ask for a
/// connection when they need one and this class reconnects lazily, which keeps the failure
/// handling explicit and the same for the very first connection and later reconnections.
/// </summary>
public sealed partial class RabbitMqConnection(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnection> logger) : IRabbitMqConnection, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public bool IsConnected => _connection?.IsOpen == true;

    public async Task<IConnection> GetAsync(CancellationToken cancellationToken)
    {
        var current = _connection;
        if (current?.IsOpen == true)
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection?.IsOpen == true)
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
                _connection = null;
            }

            var settings = options.Value;
            var factory = new ConnectionFactory
            {
                Uri = new Uri(settings.Uri),
                ClientProvidedName = settings.ClientName,
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false,
                RequestedConnectionTimeout = settings.OperationTimeout,
            };

            _connection = await factory.CreateConnectionAsync(cancellationToken);
            _connection.ConnectionShutdownAsync += (_, args) =>
            {
                LogConnectionLost(args.ReplyText);
                return Task.CompletedTask;
            };

            LogConnected(factory.Endpoint.ToString());
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _gate.Dispose();
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to RabbitMQ at {Endpoint}")]
    private partial void LogConnected(string endpoint);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RabbitMQ connection lost: {Reason}")]
    private partial void LogConnectionLost(string reason);
}
