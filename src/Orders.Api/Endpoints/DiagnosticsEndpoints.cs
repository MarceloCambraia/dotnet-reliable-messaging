using Microsoft.EntityFrameworkCore;
using Orders.Infrastructure.Persistence;

namespace Orders.Api.Endpoints;

public sealed record OutboxStats(int Pending, int Processed, int Failed, DateTimeOffset? OldestPendingAt);

internal static class DiagnosticsEndpoints
{
    /// <summary>Read-only view of the outbox, used by the chaos demo to watch messages pile up and drain.</summary>
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/diagnostics/outbox", async (OrdersDbContext db, CancellationToken cancellationToken) =>
            {
                var messages = db.OutboxMessages.AsNoTracking();
                var pending = messages.Where(m => m.ProcessedAt == null && m.FailedAt == null);

                return new OutboxStats(
                    await pending.CountAsync(cancellationToken),
                    await messages.CountAsync(m => m.ProcessedAt != null, cancellationToken),
                    await messages.CountAsync(m => m.FailedAt != null, cancellationToken),
                    await pending.MinAsync(m => (DateTimeOffset?)m.OccurredAt, cancellationToken));
            })
            .WithTags("Diagnostics")
            .WithName("GetOutboxStats");

        return app;
    }
}
