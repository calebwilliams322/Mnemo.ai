using Microsoft.Extensions.Logging;
using Mnemo.Domain.Enums;

namespace Mnemo.Application.Services;

/// <summary>
/// Unified service for sending real-time notifications via both SignalR (to frontend) and webhooks (to external systems).
/// </summary>
public interface IEventNotificationService
{
    Task NotifyDocumentUploadedAsync(Guid tenantId, Guid documentId, string fileName, CancellationToken cancellationToken = default);
    Task NotifyDocumentProcessingAsync(Guid tenantId, Guid documentId, string stage, int progressPercent, CancellationToken cancellationToken = default);
    Task NotifyDocumentProcessedAsync(Guid tenantId, Guid documentId, string fileName, List<Guid> policyIds, List<string> coverageTypes, decimal confidence, CancellationToken cancellationToken = default);
    Task NotifyDocumentFailedAsync(Guid tenantId, Guid documentId, string fileName, string errorCode, string errorMessage, CancellationToken cancellationToken = default);
    Task NotifyPolicyExtractedAsync(Guid tenantId, Guid policyId, Guid documentId, string? policyNumber, string? carrierName, string? insuredName, CancellationToken cancellationToken = default);
    Task NotifyComplianceCheckCompletedAsync(Guid tenantId, Guid checkId, bool isCompliant, int gapCount, decimal complianceScore, CancellationToken cancellationToken = default);
    Task NotifyGapAnalysisCompletedAsync(Guid tenantId, Guid analysisId, decimal score, int gapsFound, CancellationToken cancellationToken = default);
}

/// <summary>
/// SignalR hub context interface for dependency injection
/// </summary>
public interface IMnemoHubContext
{
    Task SendToTenantAsync(string tenantId, string method, object payload, CancellationToken cancellationToken = default);
    Task SendToDocumentSubscribersAsync(string documentId, string method, object payload, CancellationToken cancellationToken = default);
}

public class EventNotificationService : IEventNotificationService
{
    private readonly IWebhookService _webhookService;
    private readonly IMnemoHubContext _hubContext;
    private readonly ILogger<EventNotificationService> _logger;

    public EventNotificationService(
        IWebhookService webhookService,
        IMnemoHubContext hubContext,
        ILogger<EventNotificationService> logger)
    {
        _webhookService = webhookService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyDocumentUploadedAsync(Guid tenantId, Guid documentId, string fileName, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            document_id = documentId,
            file_name = fileName
        };

        var signalRPayload = new
        {
            DocumentId = documentId,
            FileName = fileName,
            Timestamp = DateTime.UtcNow
        };

        await Task.WhenAll(
            SafeFireWebhookAsync(tenantId, WebhookEvent.DocumentUploaded, payload, cancellationToken),
            SafeSendSignalRAsync(tenantId.ToString(), "DocumentUploaded", signalRPayload, cancellationToken)
        );
    }

    public async Task NotifyDocumentProcessingAsync(Guid tenantId, Guid documentId, string stage, int progressPercent, CancellationToken cancellationToken = default)
    {
        // Processing updates are only sent via SignalR (too chatty for webhooks)
        var signalRPayload = new
        {
            DocumentId = documentId,
            Stage = stage,
            ProgressPercent = progressPercent,
            Timestamp = DateTime.UtcNow
        };

        await SafeSendSignalRAsync(tenantId.ToString(), "DocumentProcessing", signalRPayload, cancellationToken);

        // Also send to document-specific subscribers
        await SafeSendToDocumentAsync(documentId.ToString(), "DocumentProcessing", signalRPayload, cancellationToken);
    }

    public async Task NotifyDocumentProcessedAsync(Guid tenantId, Guid documentId, string fileName, List<Guid> policyIds, List<string> coverageTypes, decimal confidence, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            document_id = documentId,
            file_name = fileName,
            policy_ids = policyIds,
            coverages_extracted = coverageTypes,
            extraction_confidence = confidence
        };

        var signalRPayload = new
        {
            DocumentId = documentId,
            FileName = fileName,
            PolicyIds = policyIds,
            CoverageTypes = coverageTypes,
            ExtractionConfidence = confidence,
            Timestamp = DateTime.UtcNow
        };

        await Task.WhenAll(
            SafeFireWebhookAsync(tenantId, WebhookEvent.DocumentProcessed, payload, cancellationToken),
            SafeSendSignalRAsync(tenantId.ToString(), "DocumentProcessed", signalRPayload, cancellationToken),
            SafeSendToDocumentAsync(documentId.ToString(), "DocumentProcessed", signalRPayload, cancellationToken)
        );
    }

    public async Task NotifyDocumentFailedAsync(Guid tenantId, Guid documentId, string fileName, string errorCode, string errorMessage, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            document_id = documentId,
            file_name = fileName,
            error_code = errorCode,
            error_message = errorMessage
        };

        var signalRPayload = new
        {
            DocumentId = documentId,
            FileName = fileName,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Timestamp = DateTime.UtcNow
        };

        await Task.WhenAll(
            SafeFireWebhookAsync(tenantId, WebhookEvent.DocumentFailed, payload, cancellationToken),
            SafeSendSignalRAsync(tenantId.ToString(), "DocumentFailed", signalRPayload, cancellationToken),
            SafeSendToDocumentAsync(documentId.ToString(), "DocumentFailed", signalRPayload, cancellationToken)
        );
    }

    public async Task NotifyPolicyExtractedAsync(Guid tenantId, Guid policyId, Guid documentId, string? policyNumber, string? carrierName, string? insuredName, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            policy_id = policyId,
            document_id = documentId,
            policy_number = policyNumber,
            carrier_name = carrierName,
            insured_name = insuredName
        };

        var signalRPayload = new
        {
            PolicyId = policyId,
            DocumentId = documentId,
            PolicyNumber = policyNumber,
            CarrierName = carrierName,
            InsuredName = insuredName,
            Timestamp = DateTime.UtcNow
        };

        await Task.WhenAll(
            SafeFireWebhookAsync(tenantId, WebhookEvent.PolicyExtracted, payload, cancellationToken),
            SafeSendSignalRAsync(tenantId.ToString(), "PolicyExtracted", signalRPayload, cancellationToken)
        );
    }

    public async Task NotifyComplianceCheckCompletedAsync(Guid tenantId, Guid checkId, bool isCompliant, int gapCount, decimal complianceScore, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            check_id = checkId,
            is_compliant = isCompliant,
            gap_count = gapCount,
            compliance_score = complianceScore
        };

        var signalRPayload = new
        {
            CheckId = checkId,
            IsCompliant = isCompliant,
            GapCount = gapCount,
            ComplianceScore = complianceScore,
            Timestamp = DateTime.UtcNow
        };

        await Task.WhenAll(
            SafeFireWebhookAsync(tenantId, WebhookEvent.ComplianceCheckCompleted, payload, cancellationToken),
            SafeSendSignalRAsync(tenantId.ToString(), "ComplianceCheckCompleted", signalRPayload, cancellationToken)
        );
    }

    public async Task NotifyGapAnalysisCompletedAsync(Guid tenantId, Guid analysisId, decimal score, int gapsFound, CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            analysis_id = analysisId,
            score = score,
            gaps_found = gapsFound
        };

        var signalRPayload = new
        {
            AnalysisId = analysisId,
            Score = score,
            GapsFound = gapsFound,
            Timestamp = DateTime.UtcNow
        };

        await Task.WhenAll(
            SafeFireWebhookAsync(tenantId, WebhookEvent.GapAnalysisCompleted, payload, cancellationToken),
            SafeSendSignalRAsync(tenantId.ToString(), "GapAnalysisCompleted", signalRPayload, cancellationToken)
        );
    }

    private async Task SafeFireWebhookAsync(Guid tenantId, WebhookEvent eventType, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await _webhookService.FireWebhookAsync(tenantId, eventType, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fire webhook for event {Event}", eventType);
        }
    }

    private async Task SafeSendSignalRAsync(string tenantId, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.SendToTenantAsync(tenantId, method, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SignalR message for method {Method}", method);
        }
    }

    private async Task SafeSendToDocumentAsync(string documentId, string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await _hubContext.SendToDocumentSubscribersAsync(documentId, method, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SignalR message to document subscribers for method {Method}", method);
        }
    }
}
