using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;

namespace Mnemo.Application.Services;

public interface IWebhookService
{
    // CRUD operations
    Task<WebhookDto> CreateWebhookAsync(Guid tenantId, CreateWebhookRequest request, CancellationToken cancellationToken = default);
    Task<WebhookDto?> GetWebhookAsync(Guid tenantId, Guid webhookId, CancellationToken cancellationToken = default);
    Task<List<WebhookDto>> GetWebhooksAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<WebhookDto?> UpdateWebhookAsync(Guid tenantId, Guid webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken = default);
    Task<bool> DeleteWebhookAsync(Guid tenantId, Guid webhookId, CancellationToken cancellationToken = default);
    Task<List<WebhookDeliveryDto>> GetDeliveryLogsAsync(Guid tenantId, Guid webhookId, int limit = 20, string? status = null, CancellationToken cancellationToken = default);

    // Delivery operations
    Task FireWebhookAsync(Guid tenantId, WebhookEvent eventType, object payload, CancellationToken cancellationToken = default);
    Task ProcessPendingDeliveriesAsync(CancellationToken cancellationToken = default);
}

// DTOs
public record CreateWebhookRequest(
    string Url,
    List<string> Events,
    string? Secret = null
);

public record UpdateWebhookRequest(
    string? Url = null,
    List<string>? Events = null,
    bool? IsActive = null
);

public record WebhookDto(
    Guid Id,
    string Url,
    List<string> Events,
    bool IsActive,
    int ConsecutiveFailures,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? LastTriggeredAt
);

public record WebhookDeliveryDto(
    Guid Id,
    string Event,
    string Status,
    int? ResponseStatusCode,
    int AttemptCount,
    DateTime CreatedAt,
    DateTime? DeliveredAt
);

public class WebhookService : IWebhookService
{
    private readonly DbContext _dbContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookService> _logger;

    // Retry delays: 1s, 10s, 60s
    private static readonly int[] RetryDelaysSeconds = { 1, 10, 60 };
    private const int MaxRetries = 3;
    private const int TimeoutSeconds = 10;
    private const int MaxConsecutiveFailures = 10;

    public WebhookService(DbContext dbContext, IHttpClientFactory httpClientFactory, ILogger<WebhookService> logger)
    {
        _dbContext = dbContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<WebhookDto> CreateWebhookAsync(Guid tenantId, CreateWebhookRequest request, CancellationToken cancellationToken = default)
    {
        // Validate URL
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            throw new ArgumentException("Invalid webhook URL. Must be a valid HTTP(S) URL.");
        }

        var webhook = new Webhook
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Url = request.Url,
            Secret = request.Secret,
            Events = JsonSerializer.Serialize(request.Events),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Set<Webhook>().Add(webhook);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(webhook);
    }

    public async Task<WebhookDto?> GetWebhookAsync(Guid tenantId, Guid webhookId, CancellationToken cancellationToken = default)
    {
        var webhook = await _dbContext.Set<Webhook>()
            .FirstOrDefaultAsync(w => w.Id == webhookId && w.TenantId == tenantId, cancellationToken);

        return webhook == null ? null : MapToDto(webhook);
    }

    public async Task<List<WebhookDto>> GetWebhooksAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var webhooks = await _dbContext.Set<Webhook>()
            .Where(w => w.TenantId == tenantId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        return webhooks.Select(MapToDto).ToList();
    }

    public async Task<WebhookDto?> UpdateWebhookAsync(Guid tenantId, Guid webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken = default)
    {
        var webhook = await _dbContext.Set<Webhook>()
            .FirstOrDefaultAsync(w => w.Id == webhookId && w.TenantId == tenantId, cancellationToken);

        if (webhook == null)
            return null;

        if (!string.IsNullOrEmpty(request.Url))
        {
            if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new ArgumentException("Invalid webhook URL. Must be a valid HTTP(S) URL.");
            }
            webhook.Url = request.Url;
        }

        if (request.Events != null)
        {
            webhook.Events = JsonSerializer.Serialize(request.Events);
        }

        if (request.IsActive.HasValue)
        {
            webhook.IsActive = request.IsActive.Value;
            // Reset failure count when re-enabling
            if (request.IsActive.Value)
            {
                webhook.ConsecutiveFailures = 0;
            }
        }

        webhook.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapToDto(webhook);
    }

    public async Task<bool> DeleteWebhookAsync(Guid tenantId, Guid webhookId, CancellationToken cancellationToken = default)
    {
        var webhook = await _dbContext.Set<Webhook>()
            .FirstOrDefaultAsync(w => w.Id == webhookId && w.TenantId == tenantId, cancellationToken);

        if (webhook == null)
            return false;

        _dbContext.Set<Webhook>().Remove(webhook);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<WebhookDeliveryDto>> GetDeliveryLogsAsync(Guid tenantId, Guid webhookId, int limit = 20, string? status = null, CancellationToken cancellationToken = default)
    {
        // Verify webhook belongs to tenant
        var webhookExists = await _dbContext.Set<Webhook>()
            .AnyAsync(w => w.Id == webhookId && w.TenantId == tenantId, cancellationToken);

        if (!webhookExists)
            return new List<WebhookDeliveryDto>();

        var query = _dbContext.Set<WebhookDelivery>()
            .Where(d => d.WebhookId == webhookId);

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<WebhookDeliveryStatus>(status, true, out var statusEnum))
        {
            query = query.Where(d => d.Status == statusEnum);
        }

        var deliveries = await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .Select(d => new WebhookDeliveryDto(
                d.Id,
                d.Event.ToString(),
                d.Status.ToString(),
                d.ResponseStatusCode,
                d.AttemptCount,
                d.CreatedAt,
                d.DeliveredAt
            ))
            .ToListAsync(cancellationToken);

        return deliveries;
    }

    public async Task FireWebhookAsync(Guid tenantId, WebhookEvent eventType, object payload, CancellationToken cancellationToken = default)
    {
        // Get all active webhooks for this tenant that subscribe to this event
        var webhooks = await _dbContext.Set<Webhook>()
            .Where(w => w.TenantId == tenantId && w.IsActive)
            .ToListAsync(cancellationToken);

        var eventName = eventType.ToString();

        foreach (var webhook in webhooks)
        {
            var subscribedEvents = JsonSerializer.Deserialize<List<string>>(webhook.Events) ?? new List<string>();

            // Check if webhook subscribes to this event or all events (*)
            if (!subscribedEvents.Contains("*") && !subscribedEvents.Contains(eventName))
                continue;

            // Create delivery record
            var delivery = new WebhookDelivery
            {
                Id = Guid.NewGuid(),
                WebhookId = webhook.Id,
                Event = eventType,
                Payload = JsonSerializer.Serialize(new WebhookPayload(
                    Id: Guid.NewGuid().ToString("N"),
                    Event: eventName,
                    Timestamp: DateTime.UtcNow,
                    TenantId: tenantId,
                    Data: payload
                )),
                Status = WebhookDeliveryStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Set<WebhookDelivery>().Add(delivery);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Attempt immediate delivery (will be picked up by background job if it fails)
        await ProcessPendingDeliveriesAsync(cancellationToken);
    }

    public async Task ProcessPendingDeliveriesAsync(CancellationToken cancellationToken = default)
    {
        // Get pending deliveries that are ready to be attempted
        var pendingDeliveries = await _dbContext.Set<WebhookDelivery>()
            .Include(d => d.Webhook)
            .Where(d => d.Status == WebhookDeliveryStatus.Pending &&
                       (d.NextRetryAt == null || d.NextRetryAt <= DateTime.UtcNow) &&
                       d.Webhook.IsActive)
            .Take(50) // Process in batches
            .ToListAsync(cancellationToken);

        foreach (var delivery in pendingDeliveries)
        {
            await AttemptDeliveryAsync(delivery, cancellationToken);
        }
    }

    private async Task AttemptDeliveryAsync(WebhookDelivery delivery, CancellationToken cancellationToken)
    {
        var webhook = delivery.Webhook;
        delivery.AttemptCount++;

        try
        {
            var client = _httpClientFactory.CreateClient("webhook");
            client.Timeout = TimeSpan.FromSeconds(TimeoutSeconds);

            var request = new HttpRequestMessage(HttpMethod.Post, webhook.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
            };

            // Add signature header if secret is configured
            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                var signature = ComputeSignature(delivery.Payload, webhook.Secret);
                request.Headers.Add("X-Mnemo-Signature", $"sha256={signature}");
            }

            request.Headers.Add("X-Mnemo-Event", delivery.Event.ToString());
            request.Headers.Add("X-Mnemo-Delivery-Id", delivery.Id.ToString());

            var response = await client.SendAsync(request, cancellationToken);
            delivery.ResponseStatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = WebhookDeliveryStatus.Success;
                delivery.DeliveredAt = DateTime.UtcNow;
                webhook.ConsecutiveFailures = 0;
                webhook.UpdatedAt = DateTime.UtcNow;

                _logger.LogInformation("Webhook delivered successfully: {WebhookId} -> {Url}",
                    webhook.Id, webhook.Url);
            }
            else
            {
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                delivery.ResponseBody = responseBody.Length > 1000 ? responseBody[..1000] : responseBody;
                HandleDeliveryFailure(delivery, webhook, $"HTTP {delivery.ResponseStatusCode}");
            }
        }
        catch (TaskCanceledException)
        {
            HandleDeliveryFailure(delivery, webhook, "Request timeout");
        }
        catch (HttpRequestException ex)
        {
            delivery.ErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            HandleDeliveryFailure(delivery, webhook, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error delivering webhook {WebhookId}", webhook.Id);
            delivery.ErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            HandleDeliveryFailure(delivery, webhook, "Unexpected error");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void HandleDeliveryFailure(WebhookDelivery delivery, Webhook webhook, string reason)
    {
        webhook.ConsecutiveFailures++;
        webhook.UpdatedAt = DateTime.UtcNow;

        if (delivery.AttemptCount >= MaxRetries)
        {
            delivery.Status = WebhookDeliveryStatus.Failed;
            _logger.LogWarning("Webhook delivery failed after {Attempts} attempts: {WebhookId} - {Reason}",
                delivery.AttemptCount, webhook.Id, reason);
        }
        else
        {
            // Schedule retry
            var delayIndex = Math.Min(delivery.AttemptCount - 1, RetryDelaysSeconds.Length - 1);
            delivery.NextRetryAt = DateTime.UtcNow.AddSeconds(RetryDelaysSeconds[delayIndex]);
            _logger.LogInformation("Webhook delivery will retry in {Seconds}s: {WebhookId}",
                RetryDelaysSeconds[delayIndex], webhook.Id);
        }

        // Disable webhook after too many consecutive failures
        if (webhook.ConsecutiveFailures >= MaxConsecutiveFailures)
        {
            webhook.IsActive = false;
            _logger.LogWarning("Webhook disabled after {Failures} consecutive failures: {WebhookId}",
                webhook.ConsecutiveFailures, webhook.Id);
        }
    }

    private static string ComputeSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static WebhookDto MapToDto(Webhook webhook)
    {
        var events = JsonSerializer.Deserialize<List<string>>(webhook.Events) ?? new List<string>();
        var lastTriggered = webhook.Deliveries?.MaxBy(d => d.CreatedAt)?.CreatedAt;

        return new WebhookDto(
            webhook.Id,
            webhook.Url,
            events,
            webhook.IsActive,
            webhook.ConsecutiveFailures,
            webhook.CreatedAt,
            webhook.UpdatedAt,
            lastTriggered
        );
    }
}

// Webhook payload wrapper
public record WebhookPayload(
    string Id,
    string Event,
    DateTime Timestamp,
    Guid TenantId,
    object Data
);
