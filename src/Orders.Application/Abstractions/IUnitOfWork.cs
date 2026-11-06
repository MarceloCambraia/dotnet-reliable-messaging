namespace Orders.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>Commits all staged changes (aggregates and outbox messages) atomically.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
