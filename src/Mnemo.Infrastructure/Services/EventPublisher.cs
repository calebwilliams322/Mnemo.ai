using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Domain.Events;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// In-memory event publisher that dispatches events to all registered handlers.
/// Handlers are resolved from DI container.
///
/// This is synchronous for simplicity. Can be replaced with a message queue
/// (RabbitMQ, Azure Service Bus, etc.) for async/distributed processing.
/// </summary>
public class EventPublisher : IEventPublisher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventPublisher> _logger;

    public EventPublisher(IServiceProvider serviceProvider, ILogger<EventPublisher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent domainEvent) where TEvent : IDomainEvent
    {
        var eventType = typeof(TEvent).Name;

        _logger.LogInformation(
            "Publishing event {EventType} (ID: {EventId}, Tenant: {TenantId})",
            eventType, domainEvent.EventId, domainEvent.TenantId);

        // Resolve all handlers for this event type
        var handlerType = typeof(IEventHandler<>).MakeGenericType(typeof(TEvent));
        var handlers = _serviceProvider.GetServices(handlerType);

        var handlerCount = 0;
        foreach (var handler in handlers)
        {
            if (handler == null) continue;

            try
            {
                var handleMethod = handlerType.GetMethod("HandleAsync");
                if (handleMethod != null)
                {
                    var task = (Task?)handleMethod.Invoke(handler, new object[] { domainEvent });
                    if (task != null)
                    {
                        await task;
                    }
                }
                handlerCount++;
            }
            catch (Exception ex)
            {
                // Log but don't throw - one handler failure shouldn't stop others
                _logger.LogError(ex,
                    "Handler {HandlerType} failed for event {EventType}",
                    handler.GetType().Name, eventType);
            }
        }

        _logger.LogDebug(
            "Event {EventType} dispatched to {HandlerCount} handlers",
            eventType, handlerCount);
    }
}
