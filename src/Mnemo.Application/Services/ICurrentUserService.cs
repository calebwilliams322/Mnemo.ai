namespace Mnemo.Application.Services;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    Guid? TenantId { get; }
    string? Email { get; }
    string? Role { get; }
    string? SupabaseUserId { get; }
    bool IsAuthenticated { get; }
    bool IsAdmin { get; }
}
