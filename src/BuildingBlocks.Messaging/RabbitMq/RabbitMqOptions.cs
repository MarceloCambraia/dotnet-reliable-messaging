namespace BuildingBlocks.Messaging.RabbitMq;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    /// <summary>AMQP URI, e.g. <c>amqp://guest:guest@localhost:5672/</c>.</summary>
    public string Uri { get; set; } = "amqp://guest:guest@localhost:5672/";

    /// <summary>Shown in the management UI, which makes connections easy to tell apart.</summary>
    public string ClientName { get; set; } = AppDomain.CurrentDomain.FriendlyName;

    /// <summary>Upper bound for connecting and for waiting on a publisher confirm.</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
