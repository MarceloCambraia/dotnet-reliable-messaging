namespace Orders.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Runs the publisher in this process. Turn off to run it elsewhere (or in tests).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Topic exchange that receives every integration event; the routing key is the message type.</summary>
    public string Exchange { get; set; } = "orders.events";

    public int BatchSize { get; set; } = 50;

    /// <summary>Idle wait between polls when the previous batch was not full.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Attempts per message before it is parked as failed (message-level errors only).</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>First retry delay for a message; doubled on each attempt, with jitter.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Upper bound for the publisher's own backoff while the broker is unreachable.</summary>
    public TimeSpan MaxBrokerBackoff { get; set; } = TimeSpan.FromSeconds(30);
}
