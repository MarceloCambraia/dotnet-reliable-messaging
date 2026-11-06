using Orders.Domain;

namespace Orders.Application.Abstractions;

public interface IOrderRepository
{
    void Add(Order order);

    Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken);
}
