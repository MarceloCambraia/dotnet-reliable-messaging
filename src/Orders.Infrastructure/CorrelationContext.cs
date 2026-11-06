namespace Orders.Infrastructure;

/// <summary>
/// Scoped holder for the correlation id of the current operation. The API fills it from the
/// <c>X-Correlation-Id</c> header; otherwise a new id is generated.
/// </summary>
public sealed class CorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";

    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
}
