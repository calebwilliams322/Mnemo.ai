namespace Mnemo.Domain.Entities;

public class ComplianceCheck
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ContractRequirementId { get; set; }

    // Policy IDs being checked (JSONB array)
    public required string PolicyIds { get; set; } = "[]";

    // Results
    public bool? IsCompliant { get; set; }
    public decimal? ComplianceScore { get; set; }

    // Detailed gaps (JSONB)
    public required string Gaps { get; set; } = "[]";

    // AI-generated summary
    public string? Summary { get; set; }

    public DateTime CheckedAt { get; set; }
    public Guid? CheckedByUserId { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ContractRequirement ContractRequirement { get; set; } = null!;
    public User? CheckedByUser { get; set; }
}
