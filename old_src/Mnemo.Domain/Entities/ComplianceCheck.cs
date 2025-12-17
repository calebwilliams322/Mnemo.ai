namespace Mnemo.Domain.Entities;

public class ComplianceCheck
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContractRequirementId { get; set; }

    // Policies being checked (stored as JSON array of GUIDs)
    public string PolicyIds { get; set; } = "[]";

    // Results
    public bool? IsCompliant { get; set; }
    public decimal? ComplianceScore { get; set; } // 0.00 to 1.00

    // Detailed gaps (JSON array)
    public string Gaps { get; set; } = "[]";

    // AI-generated summary
    public string? Summary { get; set; }

    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public Guid? CheckedByUserId { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ContractRequirement ContractRequirement { get; set; } = null!;
    public User? CheckedByUser { get; set; }
}
