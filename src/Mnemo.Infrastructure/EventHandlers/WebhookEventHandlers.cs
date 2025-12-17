using Mnemo.Application.DTOs;
using Mnemo.Application.Services;
using Mnemo.Domain.Events;

namespace Mnemo.Infrastructure.EventHandlers;

/// <summary>
/// Handles DocumentUploadedEvent by firing webhooks.
/// </summary>
public class WebhookDocumentUploadedHandler : IEventHandler<DocumentUploadedEvent>
{
    private readonly IWebhookService _webhookService;

    public WebhookDocumentUploadedHandler(IWebhookService webhookService)
    {
        _webhookService = webhookService;
    }

    public async Task HandleAsync(DocumentUploadedEvent domainEvent)
    {
        if (!domainEvent.TenantId.HasValue) return;

        await _webhookService.QueueWebhookAsync(
            domainEvent.TenantId.Value,
            WebhookEventTypes.DocumentUploaded,
            new
            {
                eventId = domainEvent.EventId,
                occurredAt = domainEvent.OccurredAt,
                documentId = domainEvent.DocumentId,
                fileName = domainEvent.FileName,
                storagePath = domainEvent.StoragePath
            });
    }
}

/// <summary>
/// Handles DocumentProcessingStartedEvent by firing webhooks.
/// </summary>
public class WebhookDocumentProcessingStartedHandler : IEventHandler<DocumentProcessingStartedEvent>
{
    private readonly IWebhookService _webhookService;

    public WebhookDocumentProcessingStartedHandler(IWebhookService webhookService)
    {
        _webhookService = webhookService;
    }

    public async Task HandleAsync(DocumentProcessingStartedEvent domainEvent)
    {
        if (!domainEvent.TenantId.HasValue) return;

        await _webhookService.QueueWebhookAsync(
            domainEvent.TenantId.Value,
            WebhookEventTypes.DocumentProcessingStarted,
            new
            {
                eventId = domainEvent.EventId,
                occurredAt = domainEvent.OccurredAt,
                documentId = domainEvent.DocumentId
            });
    }
}

/// <summary>
/// Handles DocumentProcessedEvent by firing webhooks.
/// </summary>
public class WebhookDocumentProcessedHandler : IEventHandler<DocumentProcessedEvent>
{
    private readonly IWebhookService _webhookService;

    public WebhookDocumentProcessedHandler(IWebhookService webhookService)
    {
        _webhookService = webhookService;
    }

    public async Task HandleAsync(DocumentProcessedEvent domainEvent)
    {
        if (!domainEvent.TenantId.HasValue) return;

        await _webhookService.QueueWebhookAsync(
            domainEvent.TenantId.Value,
            WebhookEventTypes.DocumentProcessed,
            new
            {
                eventId = domainEvent.EventId,
                occurredAt = domainEvent.OccurredAt,
                documentId = domainEvent.DocumentId,
                success = domainEvent.Success,
                error = domainEvent.Error
            });
    }
}
