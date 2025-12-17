namespace Mnemo.Domain.Events;

/// <summary>
/// Marker interface for domain events.
/// Events are published when significant things happen (document uploaded, processing complete, etc.)
/// Handlers can subscribe to react (SignalR broadcasts, webhooks, etc.)
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredAt { get; }
    Guid? TenantId { get; }
}

/// <summary>
/// Base class for domain events with common properties.
/// </summary>
public abstract record DomainEventBase : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
    public Guid? TenantId { get; init; }
}
