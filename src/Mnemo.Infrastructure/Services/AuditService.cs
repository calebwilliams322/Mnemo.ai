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
                Details = details != null ? JsonSerializer.Serialize(details) : null,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.AuditEvents.Add(auditEvent);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Audit: {EventType} - {EventStatus} for User:{UserId} Tenant:{TenantId} from IP:{IpAddress}",
                eventType, eventStatus, userId, tenantId, ipAddress);
        }
        catch (Exception ex)
        {
            // Never let audit logging failures break the main flow
            _logger.LogError(ex,
                "Failed to log audit event: {EventType} - {EventStatus}",
                eventType, eventStatus);
        }
    }
}
