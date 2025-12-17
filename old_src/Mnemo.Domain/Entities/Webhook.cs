namespace Mnemo.Domain.Entities;

public class Webhook
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }

    public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; } // for HMAC signature

    // Events to subscribe to (JSON array of event names)
    public string Events { get; set; } = "[]";

    public bool IsActive { get; set; } = true;
    public int ConsecutiveFailures { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = null!;
    public ICollection<WebhookDelivery> Deliveries { get; set; } = new List<WebhookDelivery>();
}
