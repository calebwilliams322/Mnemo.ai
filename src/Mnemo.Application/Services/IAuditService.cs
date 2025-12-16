namespace Mnemo.Application.Services;

public interface IAuditService
{
    Task LogEventAsync(
        string eventType,
        string eventStatus,
        Guid? tenantId = null,
        Guid? userId = null,
        string? ipAddress = null,
        string? userAgent = null,
        object? details = null);
}
