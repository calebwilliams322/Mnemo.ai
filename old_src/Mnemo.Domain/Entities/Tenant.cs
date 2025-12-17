using Mnemo.Domain.Enums;

namespace Mnemo.Domain.Entities;

public class Tenant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    // Contact
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }

    // Subscription
    public TenantPlan Plan { get; set; } = TenantPlan.Starter;
    public bool IsActive { get; set; } = true;

    // Timestamps
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Document> Documents { get; set; } = new List<Document>();
    public ICollection<Policy> Policies { get; set; } = new List<Policy>();
    public ICollection<SubmissionGroup> SubmissionGroups { get; set; } = new List<SubmissionGroup>();
    public ICollection<ContractRequirement> ContractRequirements { get; set; } = new List<ContractRequirement>();
    public ICollection<ComplianceCheck> ComplianceChecks { get; set; } = new List<ComplianceCheck>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<Webhook> Webhooks { get; set; } = new List<Webhook>();
}
