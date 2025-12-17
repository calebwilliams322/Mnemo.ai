namespace Mnemo.Domain.Enums;

public enum WebhookEvent
{
    DocumentUploaded,
    DocumentProcessed,
    DocumentFailed,
    PolicyExtracted,
    ComplianceCheckCompleted,
    GapAnalysisCompleted
}
