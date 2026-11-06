namespace Inventory.Worker;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";

    public string Exchange { get; set; } = "orders.events";

    public string Queue { get; set; } = "inventory.order-created";

    public ushort PrefetchCount { get; set; } = 16;

    /// <summary>Delay before each retry; the count is the number of retries before the DLQ.</summary>
    public TimeSpan[]? RetryDelays { get; set; }

    /// <summary>Demo/test only: orders containing this SKU make the handler throw.</summary>
    public string? FailOnSku { get; set; }

    public IReadOnlyList<TimeSpan> EffectiveRetryDelays =>
        RetryDelays is { Length: > 0 } ? RetryDelays : [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(2)];
}
