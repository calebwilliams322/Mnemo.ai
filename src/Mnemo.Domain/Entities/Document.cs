namespace Mnemo.Domain.Entities;

public class Document
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // File info
    public required string FileName { get; set; }
    public required string StoragePath { get; set; }
    public long? FileSizeBytes { get; set; }
    public required string ContentType { get; set; } = "application/pdf";
    public int? PageCount { get; set; }

    // Classification
    public string? DocumentType { get; set; } // policy, quote, binder, endorsement, dec_page, certificate, contract

    // Processing status
    public required string ProcessingStatus { get; set; } = "pending"; // pending, processing, completed, failed
    public string? ProcessingError { get; set; }
    public DateTime? ProcessedAt { get; set; }

    // Upload metadata
    public Guid? UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }

    // Optional grouping
    public Guid? SubmissionGroupId { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public User? UploadedByUser { get; set; }
    public SubmissionGroup? SubmissionGroup { get; set; }
    public ICollection<DocumentChunk> Chunks { get; set; } = new List<DocumentChunk>();
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();
    public ICollection<ContractRequirement> ContractRequirements { get; set; } = new List<ContractRequirement>();
}
