using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Messaging;
using Orders.Application.Abstractions;
using Orders.Infrastructure.Persistence;

namespace Orders.Infrastructure.Outbox;

internal sealed class EfOutbox(OrdersDbContext db, CorrelationContext correlation, TimeProvider clock) : IOutbox
{
    public void Enqueue<TMessage>(string messageType, TMessage message)
        where TMessage : notnull
    {
        var now = clock.GetUtcNow();

        // Only tracked here; it is written by the same SaveChanges (same transaction) as the aggregate.
        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            Type = messageType,
            Payload = JsonSerializer.Serialize(message, EnvelopeSerializer.Options),
            CorrelationId = correlation.CorrelationId,
            TraceParent = Activity.Current?.Id,
            OccurredAt = now,
            NextAttemptAt = now,
        });
    }
}
