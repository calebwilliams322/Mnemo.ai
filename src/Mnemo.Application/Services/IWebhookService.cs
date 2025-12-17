namespace Mnemo.Application.Services;

/// <summary>
/// Service for delivering webhooks to registered endpoints.
/// </summary>
public interface IWebhookService
{
    /// <summary>
    /// Queue a webhook delivery for all webhooks subscribed to this event type.
    /// </summary>
    /// <param name="tenantId">Tenant to send webhooks for</param>
    /// <param name="eventType">Event type (e.g., "document.processed")</param>
    /// <param name="payload">Event payload object (will be serialized to JSON)</param>
    Task QueueWebhookAsync(Guid tenantId, string eventType, object payload);

    /// <summary>
    /// Process pending webhook deliveries. Called by Hangfire.
    /// </summary>
    Task ProcessPendingDeliveriesAsync();

    /// <summary>
    /// Attempt to deliver a specific webhook. Called by Hangfire for retries.
    /// </summary>
    Task DeliverWebhookAsync(Guid deliveryId);
}
