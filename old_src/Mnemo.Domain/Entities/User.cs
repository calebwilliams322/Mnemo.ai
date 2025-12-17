using Mnemo.Domain.Enums;

namespace Mnemo.Domain.Entities;

public class User
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    // Auth - links to Supabase auth.users
    public string? SupabaseUserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? Name { get; set; }
    public UserRole Role { get; set; } = UserRole.User;

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Document> UploadedDocuments { get; set; } = new List<Document>();
    public ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    public ICollection<ComplianceCheck> ComplianceChecks { get; set; } = new List<ComplianceCheck>();
}
