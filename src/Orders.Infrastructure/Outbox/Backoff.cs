namespace Orders.Infrastructure.Outbox;

internal static class Backoff
{
    /// <summary>
    /// Exponential backoff with ±20% jitter: <c>base * 2^(attempt-1)</c>, capped at <paramref name="max"/>.
    /// Jitter spreads retries out so that many messages failing together do not retry together.
    /// </summary>
    /// <param name="attempt">1-based number of the attempt that just failed.</param>
    public static TimeSpan Exponential(int attempt, TimeSpan @base, TimeSpan max, double jitter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);

        var exponent = Math.Min(attempt - 1, 30);
        var delay = Math.Min(@base.TotalMilliseconds * Math.Pow(2, exponent), max.TotalMilliseconds);
        var jittered = delay * (1 + ((jitter * 2) - 1) * 0.2);
        return TimeSpan.FromMilliseconds(Math.Min(jittered, max.TotalMilliseconds));
    }

    public static TimeSpan Exponential(int attempt, TimeSpan @base, TimeSpan max) =>
        Exponential(attempt, @base, max, Random.Shared.NextDouble());
}
