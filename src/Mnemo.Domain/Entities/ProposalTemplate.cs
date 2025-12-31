namespace Mnemo.Domain.Entities;

/// <summary>
/// Stores Word (.docx) templates uploaded by agencies for proposal generation.
/// Templates contain placeholders like {{insured_name}} that get filled with policy data.
/// </summary>
public class ProposalTemplate
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // Template info
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required string StoragePath { get; set; }      // Supabase path: {tenant_id}/templates/{id}.docx
    public required string OriginalFileName { get; set; }
    public long? FileSizeBytes { get; set; }

    // Extracted placeholders (stored as JSON array)
    public required string Placeholders { get; set; } = "[]";

    // Status
    public bool IsActive { get; set; } = true;
    public bool IsDefault { get; set; } = false;  // True for system-provided default template

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Proposal> Proposals { get; set; } = new List<Proposal>();
}
