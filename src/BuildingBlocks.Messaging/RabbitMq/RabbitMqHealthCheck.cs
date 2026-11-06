using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Messaging.RabbitMq;

public sealed class RabbitMqHealthCheck(IRabbitMqConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var current = await connection.GetAsync(timeout.Token);
            return current.IsOpen
                ? HealthCheckResult.Healthy("Connected to RabbitMQ.")
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed.");
        }
#pragma warning disable CA1031 // A health check reports failures instead of throwing.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "RabbitMQ is unreachable.", ex);
        }
    }
}
