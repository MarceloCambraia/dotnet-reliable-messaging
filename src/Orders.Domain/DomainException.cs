namespace Orders.Domain;

/// <summary>Raised when a business invariant is violated.</summary>
public sealed class DomainException(string message) : Exception(message);
