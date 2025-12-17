namespace Mnemo.Domain.Events;

/// <summary>
/// Published when a document is uploaded to storage.
/// </summary>
public record DocumentUploadedEvent : DomainEventBase
{
    public required Guid DocumentId { get; init; }
    public required string FileName { get; init; }
    public required string StoragePath { get; init; }
}

/// <summary>
/// Published when document processing starts.
/// </summary>
public record DocumentProcessingStartedEvent : DomainEventBase
{
    public required Guid DocumentId { get; init; }
}

/// <summary>
/// Published when document processing completes (success or failure).
/// Phase 4 webhooks will subscribe to this event.
/// </summary>
public record DocumentProcessedEvent : DomainEventBase
{
    public required Guid DocumentId { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Published during extraction to report progress.
/// </summary>
public record DocumentProgressEvent : DomainEventBase
{
    public required Guid DocumentId { get; init; }
    public required string Stage { get; init; }
    public required int ProgressPercent { get; init; }
    public string? Message { get; init; }
}

/// <summary>
/// Published when structured extraction completes (Policy + Coverages created).
/// </summary>
public record ExtractionCompletedEvent : DomainEventBase
{
    public required Guid DocumentId { get; init; }
    public Guid? PolicyId { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }
    public int CoveragesExtracted { get; init; }
}
