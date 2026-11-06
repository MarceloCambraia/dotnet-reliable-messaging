namespace Orders.Application.Abstractions;

/// <summary>
/// Stages an integration event to be published later. Nothing is sent to the broker here:
/// the message is persisted by <see cref="IUnitOfWork.SaveChangesAsync"/> in the same
/// database transaction as the aggregate, which is the whole point of the outbox.
/// </summary>
public interface IOutbox
{
    void Enqueue<TMessage>(string messageType, TMessage message)
        where TMessage : notnull;
}
