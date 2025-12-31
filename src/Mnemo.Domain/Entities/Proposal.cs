namespace Mnemo.Domain.Entities;

/// <summary>
/// Represents a generated proposal document.
/// Links a template to one or more policies and stores the generated output.
/// </summary>
public class Proposal
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid TemplateId { get; set; }

    // Client info (denormalized for easy display)
    public required string ClientName { get; set; }

    // Selected policies (stored as JSON array of Guids)
    public required string PolicyIds { get; set; } = "[]";

    // Generated document
    public string? OutputStoragePath { get; set; }  // Supabase path: {tenant_id}/proposals/{id}.docx

    // Status: pending, processing, completed, failed
    public required string Status { get; set; } = "pending";
    public string? ErrorMessage { get; set; }

    // Timestamps
    public DateTime CreatedAt { get; set; }
    public DateTime? GeneratedAt { get; set; }

    // Who created it
    public Guid? CreatedByUserId { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
    public ProposalTemplate Template { get; set; } = null!;
    public User? CreatedByUser { get; set; }
}
