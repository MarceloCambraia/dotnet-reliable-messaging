using System.Text.Json;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.RabbitMq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure.Outbox;

/// <param name="Fetched">Rows claimed in this batch.</param>
/// <param name="BrokerUnavailable">The batch stopped early because the broker could not be reached.</param>
public sealed record OutboxBatchResult(int Fetched, int Published, int Failed, bool BrokerUnavailable);

/// <summary>Claims and publishes one batch of pending outbox messages.</summary>
public sealed partial class OutboxProcessor(
    OrdersDbContext db,
    IMessagePublisher publisher,
    IOptions<OutboxOptions> options,
    TimeProvider clock,
    ILogger<OutboxProcessor> logger)
{
    private const int MaxErrorLength = 2000;

    public async Task<OutboxBatchResult> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        // The retrying execution strategy requires user transactions to run inside it; if the
        // database connection blips, the whole batch is retried (worst case: a duplicate publish,
        // which consumers already tolerate).
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(ProcessInTransactionAsync, cancellationToken);
    }

    private async Task<OutboxBatchResult> ProcessInTransactionAsync(CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var settings = options.Value;
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // FOR UPDATE locks the claimed rows until commit; SKIP LOCKED makes concurrent publishers
        // skip rows another instance already holds instead of blocking on them. Each instance
        // therefore gets a disjoint batch and no message is published twice by two instances.
        var batch = await db.OutboxMessages
            .FromSql($"""
                SELECT * FROM outbox_messages
                WHERE processed_at IS NULL AND failed_at IS NULL AND next_attempt_at <= {now}
                ORDER BY occurred_at
                LIMIT {settings.BatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);

        int published = 0, failed = 0;
        var brokerUnavailable = false;

        foreach (var message in batch)
        {
            try
            {
                await publisher.PublishAsync(settings.Exchange, message.Type, ToEnvelope(message), message.TraceParent, cancellationToken);
                message.ProcessedAt = clock.GetUtcNow();
                published++;
            }
            catch (Exception ex) when (BrokerErrors.IsConnectivityFailure(ex))
            {
                // An outage is not the message's fault: keep its attempt budget intact, stop the
                // batch and let the background service back off. Rows not yet published are released at commit.
                LogBrokerUnavailable(ex.Message);
                brokerUnavailable = true;
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RegisterFailure(message, ex, settings);
                failed++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (batch.Count > 0)
        {
            LogBatchProcessed(batch.Count, published, failed);
        }

        return new OutboxBatchResult(batch.Count, published, failed, brokerUnavailable);
    }

    private void RegisterFailure(OutboxMessage message, Exception error, OutboxOptions settings)
    {
        var now = clock.GetUtcNow();
        message.Attempts++;
        var description = $"{error.GetType().Name}: {error.Message}";
        message.LastError = description.Length <= MaxErrorLength ? description : description[..MaxErrorLength];

        if (message.Attempts >= settings.MaxAttempts)
        {
            message.FailedAt = now;
            LogMessageParked(error, message.Id, message.Attempts);
        }
        else
        {
            var delay = Backoff.Exponential(message.Attempts, settings.BaseRetryDelay, settings.MaxRetryDelay);
            message.NextAttemptAt = now + delay;
            LogMessageRetry(error, message.Id, message.Attempts, settings.MaxAttempts, delay);
        }
    }

    private static MessageEnvelope ToEnvelope(OutboxMessage message)
    {
        using var payload = JsonDocument.Parse(message.Payload);
        return new MessageEnvelope
        {
            MessageId = message.Id,
            Type = message.Type,
            CorrelationId = message.CorrelationId,
            OccurredAt = message.OccurredAt,
            Payload = payload.RootElement.Clone(),
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox batch: {Fetched} claimed, {Published} published, {Failed} failed")]
    private partial void LogBatchProcessed(int fetched, int published, int failed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Broker unavailable, pending messages stay in the outbox: {Reason}")]
    private partial void LogBrokerUnavailable(string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing outbox message {MessageId} failed (attempt {Attempt}/{MaxAttempts}); retrying in {Delay}")]
    private partial void LogMessageRetry(Exception exception, Guid messageId, int attempt, int maxAttempts, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Error, Message = "Outbox message {MessageId} parked as failed after {Attempts} attempts")]
    private partial void LogMessageParked(Exception exception, Guid messageId, int attempts);
}
