using System.Security.Claims;
using Mnemo.Application.Services;
using Mnemo.Domain.Enums;

namespace Mnemo.Api.Services;

/// <summary>
/// Extracts user information directly from JWT claims.
///
/// Design: All user info (tenant_id, role, user_id) is stored in JWT claims
/// via Supabase app_metadata. This avoids database lookups and circular
/// dependencies with EF Core query filters.
///
/// Expected JWT claims:
/// - sub: Supabase user ID
/// - email: User email
/// - app_metadata.tenant_id: Tenant GUID
/// - app_metadata.role: User role (admin/user)
/// - app_metadata.user_id: Internal user GUID
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    private bool _initialized;
    private Guid? _userId;
    private Guid? _tenantId;
    private string? _email;
    private string? _role;
    private string? _supabaseUserId;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            EnsureInitialized();
            return _userId;
        }
    }

    public Guid? TenantId
    {
        get
        {
            EnsureInitialized();
            return _tenantId;
        }
    }

    public string? Email
    {
        get
        {
            EnsureInitialized();
            return _email;
        }
    }

    public string? Role
    {
        get
        {
            EnsureInitialized();
            return _role;
        }
    }

    public string? SupabaseUserId
    {
        get
        {
            EnsureInitialized();
            return _supabaseUserId;
        }
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public bool IsAdmin
    {
        get
        {
            EnsureInitialized();
            return _role == UserRole.Admin;
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            _initialized = true;
            return;
        }

        // Get Supabase user ID from 'sub' claim
        _supabaseUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? user.FindFirst("sub")?.Value;

        // Get email
        _email = user.FindFirst(ClaimTypes.Email)?.Value
                 ?? user.FindFirst("email")?.Value;

        // Get tenant_id from claims (set via app_metadata in Supabase)
        var tenantClaim = user.FindFirst("tenant_id")?.Value
                          ?? user.FindFirst("app_metadata.tenant_id")?.Value;
        if (Guid.TryParse(tenantClaim, out var tenantId))
        {
            _tenantId = tenantId;
        }

        // Get user_id from claims (set via app_metadata in Supabase)
        var userIdClaim = user.FindFirst("user_id")?.Value
                          ?? user.FindFirst("app_metadata.user_id")?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            _userId = userId;
        }

        // Get role from claims (set via app_metadata in Supabase)
        _role = user.FindFirst("user_role")?.Value
                ?? user.FindFirst("app_metadata.role")?.Value
                ?? UserRole.User; // Default to user if not specified

        _initialized = true;
    }
}
