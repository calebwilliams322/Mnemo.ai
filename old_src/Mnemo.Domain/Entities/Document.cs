using Mnemo.Domain.Enums;

namespace Mnemo.Domain.Entities;

public class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // File info
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public long? FileSizeBytes { get; set; }
    public string ContentType { get; set; } = "application/pdf";
    public int? PageCount { get; set; }

    // Classification
    public DocumentType? DocumentType { get; set; }

    // Processing status
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;
    public string? ProcessingError { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // Metadata
    public Guid? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    // Optional grouping (e.g., multiple quotes for same submission)
    public Guid? SubmissionGroupId { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public User? UploadedByUser { get; set; }
    public SubmissionGroup? SubmissionGroup { get; set; }
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();
}
