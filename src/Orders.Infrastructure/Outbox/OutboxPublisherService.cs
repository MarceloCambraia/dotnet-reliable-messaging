using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Orders.Infrastructure.Outbox;

/// <summary>
/// Polls the outbox and publishes pending messages. Several instances can run at once (e.g.
/// API replicas); <c>FOR UPDATE SKIP LOCKED</c> keeps them from claiming the same rows.
/// </summary>
internal sealed partial class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var brokerBackoff = TimeSpan.Zero;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan delay;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<OutboxProcessor>().ProcessBatchAsync(stoppingToken);

                if (result.BrokerUnavailable)
                {
                    brokerBackoff = brokerBackoff == TimeSpan.Zero
                        ? settings.PollingInterval
                        : TimeSpan.FromTicks(Math.Min(brokerBackoff.Ticks * 2, settings.MaxBrokerBackoff.Ticks));
                    delay = brokerBackoff;
                }
                else
                {
                    brokerBackoff = TimeSpan.Zero;

                    // A full batch means there is probably more waiting: drain without sleeping.
                    delay = result.Fetched >= settings.BatchSize ? TimeSpan.Zero : settings.PollingInterval;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031 // The loop must survive database outages too.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogLoopFailed(ex);
                delay = settings.PollingInterval * 5;
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox publisher loop failed; will retry")]
    private partial void LogLoopFailed(Exception exception);
}
