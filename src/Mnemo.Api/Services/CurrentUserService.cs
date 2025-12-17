using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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

    /// <summary>
    /// Looks up user details from database using Supabase user ID.
    /// </summary>
    /// <remarks>
    /// TRADE-OFF: This method performs a synchronous database call because C# properties
    /// cannot be async. This is called once per request when user properties are accessed.
    ///
    /// For high-traffic scenarios, consider:
    /// 1. Adding an index on supabase_user_id (done - see MnemoDbContext)
    /// 2. Implementing a short-lived cache (30s TTL) to reduce DB load
    /// 3. Using async middleware to pre-populate user context before handlers
    ///
    /// Current mitigations:
    /// - Uses AsNoTracking() to reduce memory overhead
    /// - Query is simple (single-column lookup with index)
    /// - Results are cached for duration of request (_initialized flag)
    /// </remarks>
    private void LookupUserFromDatabase(string supabaseUserId)
    {
        // Resolve DbContext lazily to avoid circular DI dependency
        // Use a new scope to ensure we get a fresh context without query filters applied yet
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // IMPORTANT: Use IgnoreQueryFilters to avoid circular dependency
        // The query filter depends on CurrentUserService.TenantId, which would call this method
        var user = dbContext.Users
            .Include(u => u.Tenant)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefault(u => u.SupabaseUserId == supabaseUserId);

        if (user != null)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<CurrentUserService>>();

            // SECURITY: Users from deactivated tenants cannot access the system
            if (user.Tenant?.IsActive == false)
            {
                logger.LogWarning(
                    "User from deactivated tenant attempted access: UserId={UserId}, TenantId={TenantId}",
                    user.Id, user.TenantId);
                return; // Leave all fields null - effectively unauthorized
            }

            // SECURITY: Deactivated users cannot access the system
            // They have valid Supabase tokens but we reject them at the application level
            if (!user.IsActive)
            {
                logger.LogWarning(
                    "Deactivated user attempted access: UserId={UserId}, Email={Email}",
                    user.Id, user.Email);
                return; // Leave all fields null - effectively unauthorized
            }

            _userId = user.Id;
            _tenantId = user.TenantId;
            _role = user.Role;
            _email ??= user.Email; // Use DB email if not in JWT
        }
    }
}
