using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Infrastructure.Services;

public class AuditService : IAuditService
{
    private readonly MnemoDbContext _dbContext;
    private readonly ILogger<AuditService> _logger;

    public AuditService(MnemoDbContext dbContext, ILogger<AuditService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogEventAsync(
        string eventType,
        string eventStatus,
        Guid? tenantId = null,
        Guid? userId = null,
        string? ipAddress = null,
        string? userAgent = null,
        object? details = null)
    {
        const int maxRetries = 3;
        var detailsJson = details != null ? JsonSerializer.Serialize(details) : null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var auditEvent = new AuditEvent
                {
                    Id = Guid.NewGuid(),
                    EventType = eventType,
                    EventStatus = eventStatus,
                    TenantId = tenantId,
                    UserId = userId,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Details = detailsJson,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.AuditEvents.Add(auditEvent);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Audit: {EventType} - {EventStatus} for User:{UserId} Tenant:{TenantId} from IP:{IpAddress}",
                    eventType, eventStatus, userId, tenantId, ipAddress);

                return; // Success
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    // Final failure - log at Critical level for alerting
                    _logger.LogCritical(ex,
                        "AUDIT FAILURE after {MaxRetries} attempts: {EventType} - {EventStatus}. " +
                        "Details: {Details}",
                        maxRetries, eventType, eventStatus, detailsJson ?? "null");
                }
                else
                {
                    _logger.LogWarning(ex,
                        "Audit write attempt {Attempt} failed for {EventType}, retrying...",
                        attempt, eventType);

                    // Exponential backoff: 100ms, 200ms
                    await Task.Delay(100 * attempt);
                }
            }
        }
    }
}
