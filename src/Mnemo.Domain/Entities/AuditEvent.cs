namespace Mnemo.Domain.Entities;

public class AuditEvent
{
    public Guid Id { get; set; }

    // Optional - some events (like signup) occur before tenant/user exists
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }

    // Event classification
    public required string EventType { get; set; } // signup, login, invite, profile_update, authorization_failure
    public required string EventStatus { get; set; } // success, failure

    // Request context
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }

    // Additional details as JSON
    public string? Details { get; set; }

    // Timestamp
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public Tenant? Tenant { get; set; }
    public User? User { get; set; }
}
