using Microsoft.EntityFrameworkCore;
using Orders.Application.Abstractions;
using Orders.Domain;

namespace Orders.Infrastructure.Persistence;

internal sealed class OrderRepository(OrdersDbContext db) : IOrderRepository, IUnitOfWork
{
    public void Add(Order order) => db.Orders.Add(order);

    public Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);

    // EF Core wraps a multi-statement SaveChanges in a single transaction: the order, its items
    // and the outbox row are committed atomically.
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
