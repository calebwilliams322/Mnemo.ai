using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Mnemo.Application.Services;
using Mnemo.Domain.Enums;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Api.Services;

/// <summary>
/// Resolves current user information from JWT + database lookup.
///
/// Production pattern:
/// 1. JWT contains Supabase user ID (sub claim) - standard, stable
/// 2. On first access, look up user in database to get tenant_id, role
/// 3. Cache the result for the duration of the request
///
/// This approach:
/// - Works with any auth provider
/// - Role/tenant changes take effect immediately
/// - No dependency on beta features (auth hooks)
/// - Standard pattern used by most production SaaS
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider _serviceProvider;

    private bool _initialized;
    private Guid? _userId;
    private Guid? _tenantId;
    private string? _email;
    private string? _role;
    private string? _supabaseUserId;

    public CurrentUserService(
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider serviceProvider)
    {
        _httpContextAccessor = httpContextAccessor;
        _serviceProvider = serviceProvider;
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

        var claimsPrincipal = _httpContextAccessor.HttpContext?.User;
        if (claimsPrincipal?.Identity?.IsAuthenticated != true)
        {
            _initialized = true;
            return;
        }

        // Get Supabase user ID from 'sub' claim (standard JWT claim)
        _supabaseUserId = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                          ?? claimsPrincipal.FindFirst("sub")?.Value;

        // Get email from JWT (standard claim)
        _email = claimsPrincipal.FindFirst(ClaimTypes.Email)?.Value
                 ?? claimsPrincipal.FindFirst("email")?.Value;

        // Look up user in database to get tenant_id and role
        if (!string.IsNullOrEmpty(_supabaseUserId))
        {
            LookupUserFromDatabase(_supabaseUserId);
        }

        _initialized = true;
    }

    private void LookupUserFromDatabase(string supabaseUserId)
    {
        // Resolve DbContext lazily to avoid circular DI dependency
        // Use a new scope to ensure we get a fresh context without query filters applied yet
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // IMPORTANT: Use IgnoreQueryFilters to avoid circular dependency
        // The query filter depends on CurrentUserService.TenantId, which would call this method
        var user = dbContext.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefault(u => u.SupabaseUserId == supabaseUserId);

        if (user != null)
        {
            _userId = user.Id;
            _tenantId = user.TenantId;
            _role = user.Role;
            _email ??= user.Email; // Use DB email if not in JWT
        }
    }
}
