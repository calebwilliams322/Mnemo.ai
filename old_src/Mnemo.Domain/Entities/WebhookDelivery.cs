using Mnemo.Domain.Enums;

namespace Mnemo.Domain.Entities;

public class WebhookDelivery
{
    public Guid Id { get; set; }
    public Guid WebhookId { get; set; }

    public WebhookEvent Event { get; set; }
    public string Payload { get; set; } = "{}"; // JSON payload sent

    public WebhookDeliveryStatus Status { get; set; } = WebhookDeliveryStatus.Pending;
    public int? ResponseStatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? ErrorMessage { get; set; }

    public int AttemptCount { get; set; } = 0;
    public DateTime? NextRetryAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }

    // Navigation properties
    public Webhook Webhook { get; set; } = null!;
}
