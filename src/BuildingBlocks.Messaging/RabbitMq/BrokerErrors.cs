using System.Net.Sockets;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.Messaging.RabbitMq;

public static class BrokerErrors
{
    /// <summary>
    /// True when the failure is about reaching the broker (down, network, timeout) rather than
    /// about a specific message. Callers use this to avoid burning a message's retry budget
    /// during an outage: if the broker is down for an hour, no message should be given up on.
    /// </summary>
    public static bool IsConnectivityFailure(Exception exception) => exception switch
    {
        PublishException => false,
        BrokerUnreachableException or ConnectFailureException => true,
        OperationInterruptedException => true, // includes AlreadyClosedException
        SocketException or IOException or TimeoutException => true,
        AggregateException aggregate => aggregate.InnerExceptions.All(IsConnectivityFailure),
        _ => exception.InnerException is not null && IsConnectivityFailure(exception.InnerException),
    };
}
