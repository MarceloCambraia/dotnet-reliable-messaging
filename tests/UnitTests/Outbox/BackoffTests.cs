using Orders.Infrastructure.Outbox;

namespace UnitTests.Outbox;

public sealed class BackoffTests
{
    private static readonly TimeSpan Base = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan Max = TimeSpan.FromMinutes(5);

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(6, 64)]
    public void Delay_doubles_with_each_attempt(int attempt, int expectedSeconds) =>
        Backoff.Exponential(attempt, Base, Max, jitter: 0.5).Should().Be(TimeSpan.FromSeconds(expectedSeconds));

    [Fact]
    public void Delay_never_exceeds_the_cap()
    {
        Backoff.Exponential(50, Base, Max, jitter: 1).Should().Be(Max);
        Backoff.Exponential(int.MaxValue, Base, Max, jitter: 0.5).Should().Be(Max);
    }

    [Theory]
    [InlineData(0.0, 1.6)]
    [InlineData(1.0, 2.4)]
    public void Jitter_spreads_delay_by_twenty_percent(double jitter, double expectedSeconds) =>
        Backoff.Exponential(1, Base, Max, jitter).TotalSeconds.Should().BeApproximately(expectedSeconds, 0.001);

    [Fact]
    public void Attempt_must_be_positive()
    {
        var act = () => Backoff.Exponential(0, Base, Max, 0.5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
