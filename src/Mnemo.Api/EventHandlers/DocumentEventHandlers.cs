using Mnemo.Api.Services;
using Mnemo.Application.Services;
using Mnemo.Domain.Events;

namespace Mnemo.Api.EventHandlers;

/// <summary>
/// Handles DocumentUploadedEvent by sending SignalR notification.
/// </summary>
public class DocumentUploadedEventHandler : IEventHandler<DocumentUploadedEvent>
{
    private readonly INotificationService _notificationService;

    public DocumentUploadedEventHandler(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task HandleAsync(DocumentUploadedEvent domainEvent)
    {
        if (!domainEvent.TenantId.HasValue) return;

        await _notificationService.SendToTenantAsync(
            domainEvent.TenantId.Value,
            "DocumentUploaded",
            new
            {
                documentId = domainEvent.DocumentId,
                fileName = domainEvent.FileName,
                uploadedAt = domainEvent.OccurredAt
            });
    }
}

/// <summary>
/// Handles DocumentProcessingStartedEvent by sending SignalR notification.
/// </summary>
public class DocumentProcessingStartedEventHandler : IEventHandler<DocumentProcessingStartedEvent>
{
    private readonly INotificationService _notificationService;

    public DocumentProcessingStartedEventHandler(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task HandleAsync(DocumentProcessingStartedEvent domainEvent)
    {
        if (!domainEvent.TenantId.HasValue) return;

        // Send to both tenant and document-specific groups
        var payload = new
        {
            documentId = domainEvent.DocumentId,
            status = "processing",
            startedAt = domainEvent.OccurredAt
        };

        await Task.WhenAll(
            _notificationService.SendToTenantAsync(domainEvent.TenantId.Value, "DocumentProcessingStarted", payload),
            _notificationService.SendToDocumentAsync(domainEvent.DocumentId, "DocumentProcessingStarted", payload)
        );
    }
}

/// <summary>
/// Handles DocumentProgressEvent by sending SignalR notification.
/// </summary>
public class DocumentProgressEventHandler : IEventHandler<DocumentProgressEvent>
{
    private readonly INotificationService _notificationService;

    public DocumentProgressEventHandler(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task HandleAsync(DocumentProgressEvent domainEvent)
    {
        if (!domainEvent.TenantId.HasValue) return;

        var payload = new
        {
            documentId = domainEvent.DocumentId,
            stage = domainEvent.Stage,
            progressPercent = domainEvent.ProgressPercent,
            message = domainEvent.Message
        };

        // Progress updates go to both tenant and document groups
        await Task.WhenAll(
            _notificationService.SendToTenantAsync(domainEvent.TenantId.Value, "DocumentProgress", payload),
            _notificationService.SendToDocumentAsync(domainEvent.DocumentId, "DocumentProgress", payload)
        );
    }
}

/// <summary>
/// Handles DocumentProcessedEvent by sending SignalR notification.
/// </summary>
public class DocumentProcessedEventHandler : IEventHandler<DocumentProcessedEvent>
{
    private readonly INotificationService _notificationService;

    public DocumentProcessedEventHandler(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public async Task HandleAsync(DocumentProcessedEvent domainEvent)
    {
        if (!domainEvent.TenantId.HasValue) return;

        var payload = new
        {
            documentId = domainEvent.DocumentId,
            status = domainEvent.Success ? "completed" : "failed",
            error = domainEvent.Error,
            completedAt = domainEvent.OccurredAt,
            policyId = domainEvent.PolicyId,
            policyNumber = domainEvent.PolicyNumber,
            coverageCount = domainEvent.CoverageCount,
            confidence = domainEvent.Confidence
        };

        await Task.WhenAll(
            _notificationService.SendToTenantAsync(domainEvent.TenantId.Value, "DocumentProcessed", payload),
            _notificationService.SendToDocumentAsync(domainEvent.DocumentId, "DocumentProcessed", payload)
        );
    }
}
