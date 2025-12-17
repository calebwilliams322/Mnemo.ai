using Mnemo.Domain.Events;

namespace Mnemo.Application.Services;

/// <summary>
/// Publishes domain events to all registered handlers.
/// Used to decouple event sources (jobs) from event consumers (SignalR, webhooks).
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish an event to all registered handlers.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent;
}

/// <summary>
/// Handles domain events of a specific type.
/// Implement this interface to react to events (e.g., broadcast via SignalR, fire webhooks).
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent);
}
