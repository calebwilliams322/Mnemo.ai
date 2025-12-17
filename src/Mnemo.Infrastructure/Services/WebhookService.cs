using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Webhook delivery service with retry logic and HMAC-SHA256 signatures.
/// </summary>
public class WebhookService : IWebhookService
{
    private readonly MnemoDbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBackgroundJobService _jobService;
    private readonly ILogger<WebhookService> _logger;

    // Retry delays: 1s, 10s, 60s
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(60)
    ];

    private const int MaxRetries = 3;
    private const int MaxConsecutiveFailures = 10;

    public WebhookService(
        MnemoDbContext dbContext,
        IHttpClientFactory httpClientFactory,
        IBackgroundJobService jobService,
        ILogger<WebhookService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _jobService = jobService;
        _logger = logger;
    }

    public async Task QueueWebhookAsync(Guid tenantId, string eventType, object payload)
    {
        // Find all active webhooks for this tenant subscribed to this event
        var webhooks = await _dbContext.Webhooks
            .IgnoreQueryFilters()
            .Where(w => w.TenantId == tenantId && w.IsActive)
            .ToListAsync();

        var payloadJson = JsonSerializer.Serialize(payload);

        foreach (var webhook in webhooks)
        {
            // Check if webhook is subscribed to this event type
            var events = JsonSerializer.Deserialize<List<string>>(webhook.Events) ?? [];
            if (!events.Contains(eventType) && !events.Contains("*"))
                continue;

            // Create delivery record
            var delivery = new WebhookDelivery
            {
                Id = Guid.NewGuid(),
                WebhookId = webhook.Id,
                Event = eventType,
                Payload = payloadJson,
                Status = "pending",
                AttemptCount = 0,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.WebhookDeliveries.Add(delivery);
            await _dbContext.SaveChangesAsync();

            // Queue immediate delivery attempt
            _jobService.Enqueue<IWebhookService>(
                svc => svc.DeliverWebhookAsync(delivery.Id));

            _logger.LogInformation(
                "Queued webhook delivery {DeliveryId} for {EventType} to {Url}",
                delivery.Id, eventType, webhook.Url);
        }
    }

    public async Task ProcessPendingDeliveriesAsync()
    {
        // Find deliveries that are ready for retry
        var now = DateTime.UtcNow;
        var pendingDeliveries = await _dbContext.WebhookDeliveries
            .Where(d => d.Status == "pending" && (d.NextRetryAt == null || d.NextRetryAt <= now))
            .Take(100) // Process in batches
            .ToListAsync();

        foreach (var delivery in pendingDeliveries)
        {
            _jobService.Enqueue<IWebhookService>(
                svc => svc.DeliverWebhookAsync(delivery.Id));
        }

        _logger.LogDebug("Queued {Count} pending webhook deliveries", pendingDeliveries.Count);
    }

    public async Task DeliverWebhookAsync(Guid deliveryId)
    {
        var delivery = await _dbContext.WebhookDeliveries
            .Include(d => d.Webhook)
            .FirstOrDefaultAsync(d => d.Id == deliveryId);

        if (delivery == null)
        {
            _logger.LogWarning("Webhook delivery not found: {DeliveryId}", deliveryId);
            return;
        }

        if (delivery.Status != "pending")
        {
            _logger.LogDebug("Webhook delivery {DeliveryId} already processed: {Status}",
                deliveryId, delivery.Status);
            return;
        }

        var webhook = delivery.Webhook;
        if (!webhook.IsActive)
        {
            delivery.Status = "cancelled";
            delivery.ErrorMessage = "Webhook disabled";
            await _dbContext.SaveChangesAsync();
            return;
        }

        delivery.AttemptCount++;

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);

            // Build request
            var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url);
            request.Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json");

            // Add headers
            request.Headers.Add("X-Webhook-Event", delivery.Event);
            request.Headers.Add("X-Webhook-Delivery", delivery.Id.ToString());
            request.Headers.Add("X-Webhook-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());

            // Add HMAC signature if secret is configured
            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                var signature = ComputeSignature(delivery.Payload, webhook.Secret);
                request.Headers.Add("X-Webhook-Signature", $"sha256={signature}");
            }

            // Send request
            var response = await client.SendAsync(request);

            delivery.ResponseStatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                // Success
                delivery.Status = "delivered";
                delivery.DeliveredAt = DateTime.UtcNow;

                // Reset consecutive failures on success
                webhook.ConsecutiveFailures = 0;

                _logger.LogInformation(
                    "Webhook delivered: {DeliveryId} to {Url} (Status: {StatusCode})",
                    deliveryId, webhook.Url, delivery.ResponseStatusCode);
            }
            else
            {
                // HTTP error - read response body for debugging
                delivery.ResponseBody = await response.Content.ReadAsStringAsync();
                if (delivery.ResponseBody?.Length > 1000)
                    delivery.ResponseBody = delivery.ResponseBody[..1000];

                throw new HttpRequestException($"HTTP {delivery.ResponseStatusCode}");
            }
        }
        catch (Exception ex)
        {
            delivery.ErrorMessage = ex.Message;

            _logger.LogWarning(
                "Webhook delivery failed: {DeliveryId} (Attempt {Attempt}/{Max}): {Error}",
                deliveryId, delivery.AttemptCount, MaxRetries, ex.Message);

            // Check if we should retry
            if (delivery.AttemptCount < MaxRetries)
            {
                var delay = RetryDelays[Math.Min(delivery.AttemptCount - 1, RetryDelays.Length - 1)];
                delivery.NextRetryAt = DateTime.UtcNow.Add(delay);

                // Schedule retry
                _jobService.Schedule<IWebhookService>(
                    svc => svc.DeliverWebhookAsync(deliveryId), delay);

                _logger.LogInformation(
                    "Scheduled retry for webhook {DeliveryId} in {Delay}",
                    deliveryId, delay);
            }
            else
            {
                // Max retries reached - mark as failed
                delivery.Status = "failed";
                webhook.ConsecutiveFailures++;

                // Disable webhook after too many consecutive failures
                if (webhook.ConsecutiveFailures >= MaxConsecutiveFailures)
                {
                    webhook.IsActive = false;
                    _logger.LogWarning(
                        "Webhook {WebhookId} disabled after {Failures} consecutive failures",
                        webhook.Id, webhook.ConsecutiveFailures);
                }
            }
        }

        webhook.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    /// <summary>
    /// Compute HMAC-SHA256 signature for webhook payload.
    /// </summary>
    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
