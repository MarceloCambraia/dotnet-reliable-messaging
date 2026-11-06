using BuildingBlocks.Messaging;
using Inventory.Worker;
using Inventory.Worker.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Orders.Contracts;

namespace UnitTests.Inventory;

/// <summary>
/// Guard-clause behaviour that runs before any database access. The idempotent write path is
/// covered against real Postgres in the integration tests.
/// </summary>
public sealed class OrderCreatedHandlerTests : IDisposable
{
    // Never opened: these tests fail before the handler touches the database.
    private readonly InventoryDbContext _db = new(new DbContextOptionsBuilder<InventoryDbContext>().UseNpgsql("Host=unused").Options);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Unsupported_message_type_is_rejected_as_poison()
    {
        var envelope = MessageEnvelope.Create(Guid.NewGuid(), "orders.order-cancelled.v1", "c", DateTimeOffset.UnixEpoch, new { });

        var act = () => CreateSut().HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMessageException>().WithMessage("*Unsupported message type*");
    }

    [Fact]
    public async Task Malformed_payload_is_rejected_as_poison()
    {
        var envelope = MessageEnvelope.Create(Guid.NewGuid(), OrderCreated.MessageType, "c", DateTimeOffset.UnixEpoch, new { OrderId = "x" });

        var act = () => CreateSut().HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMessageException>();
    }

    [Fact]
    public async Task Configured_failing_sku_throws_a_retryable_failure()
    {
        var order = new OrderCreated(Guid.NewGuid(), "c", [new OrderCreatedItem("BOOM", 1)], 1m, DateTimeOffset.UnixEpoch);
        var envelope = MessageEnvelope.Create(Guid.NewGuid(), OrderCreated.MessageType, "c", DateTimeOffset.UnixEpoch, order);

        var act = () => CreateSut(failOnSku: "BOOM").HandleAsync(envelope, CancellationToken.None);

        await act.Should().ThrowAsync<SimulatedFailureException>();
    }

    private OrderCreatedHandler CreateSut(string? failOnSku = null) =>
        new(_db, Options.Create(new InventoryOptions { FailOnSku = failOnSku }), TimeProvider.System, NullLogger<OrderCreatedHandler>.Instance);
}
