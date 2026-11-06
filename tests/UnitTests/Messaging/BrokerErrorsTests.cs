using System.Net.Sockets;
using BuildingBlocks.Messaging.RabbitMq;
using RabbitMQ.Client.Exceptions;

namespace UnitTests.Messaging;

public sealed class BrokerErrorsTests
{
    public static TheoryData<Exception, bool> Cases => new()
    {
        { new BrokerUnreachableException(new IOException("refused")), true },
        { new SocketException(), true },
        { new TimeoutException(), true },
        { new OperationInterruptedException(), true },
        { new InvalidOperationException("wrapper", new SocketException()), true },
        { new PublishException(publishSequenceNumber: 1, isReturn: true), false },
        { new InvalidOperationException("bug"), false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Connectivity_failures_are_told_apart_from_message_failures(Exception exception, bool expected) =>
        BrokerErrors.IsConnectivityFailure(exception).Should().Be(expected);
}
