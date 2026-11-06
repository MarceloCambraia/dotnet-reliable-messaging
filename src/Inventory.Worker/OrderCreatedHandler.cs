using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.RabbitMq;
using Inventory.Worker.Domain;
using Inventory.Worker.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orders.Contracts;

namespace Inventory.Worker;

/// <summary>
/// Idempotent consumer: reserves stock for a new order exactly once per MessageId, however
/// many times the broker delivers it.
/// </summary>
public sealed partial class OrderCreatedHandler(
    InventoryDbContext db,
    IOptions<InventoryOptions> options,
    TimeProvider clock,
    ILogger<OrderCreatedHandler> logger) : IMessageHandler
{
    public async Task HandleAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.Type != OrderCreated.MessageType)
        {
            throw new InvalidMessageException($"Unsupported message type '{envelope.Type}'.");
        }

        var order = envelope.GetPayload<OrderCreated>();

        if (options.Value.FailOnSku is { Length: > 0 } failing && order.Items.Any(i => i.Sku == failing))
        {
            throw new SimulatedFailureException($"Simulated failure for SKU {failing}.");
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(ct => ReserveOnceAsync(envelope, order, ct), cancellationToken);
    }

    private async Task<bool> ReserveOnceAsync(MessageEnvelope envelope, OrderCreated order, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The inbox row is the idempotency guard. The primary key makes a concurrent duplicate
        // block on this insert until the first transaction finishes, and then insert nothing.
        var claimed = await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO inbox_messages (message_id, type, processed_at)
            VALUES ({envelope.MessageId}, {envelope.Type}, {now})
            ON CONFLICT (message_id) DO NOTHING
            """,
            cancellationToken);

        if (claimed == 0)
        {
            LogDuplicate(envelope.MessageId, order.OrderId);
            return false;
        }

        // Lock the product rows in a stable order (by SKU) so concurrent reservations cannot deadlock.
        var skus = order.Items.Select(i => i.Sku).Distinct().Order(StringComparer.Ordinal).ToArray();
        var products = await db.Products
            .FromSql($"SELECT * FROM products WHERE sku = ANY({skus}) ORDER BY sku FOR UPDATE")
            .ToDictionaryAsync(p => p.Sku, StringComparer.Ordinal, cancellationToken);

        var reservation = StockReservation.ForOrder(order.OrderId, order.Items, products, now);
        db.StockReservations.Add(reservation);

        // Side effect + inbox row commit together: either both happen or neither does.
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (reservation.Status == ReservationStatus.Reserved)
        {
            LogReserved(order.OrderId, reservation.Lines.Count);
        }
        else
        {
            LogRejected(order.OrderId, reservation.RejectionReason);
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate message {MessageId} for order {OrderId} ignored")]
    private partial void LogDuplicate(Guid messageId, Guid orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stock reserved for order {OrderId} ({Lines} lines)")]
    private partial void LogReserved(Guid orderId, int lines);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stock reservation rejected for order {OrderId}: {Reason}")]
    private partial void LogRejected(Guid orderId, string? reason);
}

public sealed class SimulatedFailureException(string message) : Exception(message);
