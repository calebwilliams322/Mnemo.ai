using System.Net.Http.Json;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mnemo.Api.Authorization;
using Mnemo.Api.Services;
using Mnemo.Application.Configuration;
using Mnemo.Application.DTOs;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;
using Mnemo.Infrastructure.Persistence;
using Mnemo.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
var supabaseSettings = builder.Configuration
    .GetSection(SupabaseSettings.SectionName)
    .Get<SupabaseSettings>() ?? throw new InvalidOperationException("Supabase configuration is required");

// Add services
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// Register DbContext
builder.Services.AddDbContext<MnemoDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetValue<string>("Database:ConnectionString"),
        o => o.UseVector()));

// Register Supabase settings
builder.Services.Configure<SupabaseSettings>(
    builder.Configuration.GetSection(SupabaseSettings.SectionName));

// Register CurrentUser service
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Configure named HttpClient for Supabase with proper headers and connection pooling
builder.Services.AddHttpClient("Supabase", client =>
{
    client.BaseAddress = new Uri(supabaseSettings.Url);
    client.DefaultRequestHeaders.Add("apikey", supabaseSettings.ServiceRoleKey);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseSettings.ServiceRoleKey}");
});

// Register Supabase Auth service
builder.Services.AddScoped<ISupabaseAuthService, SupabaseAuthService>();

// Register Audit service
builder.Services.AddScoped<IAuditService, AuditService>();

// Configure JWT Authentication with Supabase
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"{supabaseSettings.Url}/auth/v1",
            ValidateAudience = true,
            ValidAudience = "authenticated",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(supabaseSettings.JwtSecret)),
            ClockSkew = TimeSpan.FromSeconds(30) // Allow 30s clock drift tolerance
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("Authentication failed: {Message}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

// Configure Authorization
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthorizationPolicies.RequireTenant, policy =>
        policy.Requirements.Add(new TenantAuthorizationRequirement()));

    options.AddPolicy(AuthorizationPolicies.RequireAdmin, policy =>
    {
        policy.Requirements.Add(new TenantAuthorizationRequirement());
        policy.Requirements.Add(new AdminRequirement());
    });
});

builder.Services.AddScoped<IAuthorizationHandler, TenantAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, AdminAuthorizationHandler>();

// Configure Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Strict limit for signup - 5 requests per IP per hour
    // Uses X-Forwarded-For to get real client IP behind proxies/load balancers
    options.AddPolicy("signup", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetClientIp(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromHours(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // Moderate limit for invites - 20 per user per hour
    options.AddPolicy("invite", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? GetClientIp(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromHours(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    // General API limit - 100 requests per user per minute
    options.AddPolicy("api", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? GetClientIp(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));
});

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithTags("Health");

// ==================== Auth Endpoints ====================

// POST /auth/signup - Create a new tenant and admin user
app.MapPost("/auth/signup", async (
    SignupRequest request,
    ISupabaseAuthService supabaseAuth,
    IAuditService auditService,
    MnemoDbContext db,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    // Capture request context for audit logging
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();

    // Validate input
    if (!IsValidEmail(request.Email))
    {
        await auditService.LogEventAsync("signup", "failure", ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "invalid_email" });
        return Results.BadRequest("Valid email is required");
    }

    if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
    {
        await auditService.LogEventAsync("signup", "failure", ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "weak_password" });
        return Results.BadRequest("Password must be at least 6 characters");
    }

    if (string.IsNullOrWhiteSpace(request.CompanyName))
    {
        await auditService.LogEventAsync("signup", "failure", ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "missing_company_name" });
        return Results.BadRequest("Company name is required");
    }

    // Check if email already exists in our system
    var existingUser = await db.Users
        .IgnoreQueryFilters() // Check across all tenants
        .FirstOrDefaultAsync(u => u.Email == request.Email);

    if (existingUser != null)
    {
        await auditService.LogEventAsync("signup", "failure", ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "email_exists" });
        return Results.Conflict("An account with this email already exists");
    }

    // Start a transaction
    await using var transaction = await db.Database.BeginTransactionAsync();

    // Declare outside try block so we can clean up in catch block if needed
    SupabaseUserResult? supabaseResult = null;

    try
    {
        // 1. Create user in Supabase Auth
        supabaseResult = await supabaseAuth.CreateUserAsync(request.Email, request.Password);

        if (!supabaseResult.Success)
        {
            logger.LogWarning("Supabase user creation failed: {Error}", supabaseResult.Error);
            await auditService.LogEventAsync("signup", "failure", ipAddress: ipAddress, userAgent: userAgent,
                details: new { email = request.Email, reason = "supabase_error", error = supabaseResult.Error });
            return Results.BadRequest(supabaseResult.Error ?? "Failed to create account");
        }

        // 2. Create tenant
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = request.CompanyName,
            Plan = "free",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Tenants.Add(tenant);

        // 3. Create admin user linked to tenant
        var user = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = supabaseResult.SupabaseUserId,
            Email = request.Email,
            Name = request.UserName,
            Role = UserRole.Admin, // First user is always admin
            TenantId = tenant.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        // Commit transaction
        await transaction.CommitAsync();

        logger.LogInformation(
            "New tenant created: {TenantId} ({TenantName}) with admin user: {UserId} ({Email})",
            tenant.Id, tenant.Name, user.Id, user.Email);

        // Log successful signup
        await auditService.LogEventAsync("signup", "success",
            tenantId: tenant.Id, userId: user.Id, ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, companyName = request.CompanyName });

        return Results.Created($"/me", new SignupResponse(
            tenant.Id,
            user.Id,
            user.Email,
            "Account created successfully. Please verify your email and sign in."));
    }
    catch (Exception ex)
    {
        await transaction.RollbackAsync();

        // COMPENSATION: Delete orphaned Supabase user if DB transaction failed
        if (supabaseResult?.Success == true && !string.IsNullOrEmpty(supabaseResult.SupabaseUserId))
        {
            try
            {
                var deleted = await supabaseAuth.DeleteUserAsync(supabaseResult.SupabaseUserId);
                if (deleted)
                {
                    logger.LogWarning(
                        "Cleaned up orphaned Supabase user {SupabaseUserId} after DB failure",
                        supabaseResult.SupabaseUserId);
                }
                else
                {
                    logger.LogError(
                        "Failed to clean up orphaned Supabase user {SupabaseUserId} - manual cleanup required",
                        supabaseResult.SupabaseUserId);
                }
            }
            catch (Exception cleanupEx)
            {
                logger.LogError(cleanupEx,
                    "Exception cleaning up orphaned Supabase user {SupabaseUserId} - manual cleanup required",
                    supabaseResult.SupabaseUserId);
            }
        }

        logger.LogError(ex, "Failed to create tenant for {Email}", request.Email);
        await auditService.LogEventAsync("signup", "failure", ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "exception", error = ex.Message });
        return Results.Problem("Failed to create account. Please try again.");
    }
})
.WithName("Signup")
.WithTags("Auth")
.AllowAnonymous()
.RequireRateLimiting("signup");

// POST /auth/password-reset - Request a password reset email
app.MapPost("/auth/password-reset", async (
    PasswordResetRequest request,
    ISupabaseAuthService supabaseAuth,
    IAuditService auditService,
    MnemoDbContext db,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();

    // Validate email format
    if (!IsValidEmail(request.Email))
    {
        await auditService.LogEventAsync("password_reset", "failure", ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "invalid_email" });
        return Results.BadRequest("Valid email is required");
    }

    // Check if user exists in our system (for logging purposes, not for response)
    var user = await db.Users
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(u => u.Email == request.Email);

    // Always send the reset email via Supabase if the email looks valid
    // This prevents email enumeration attacks
    var success = await supabaseAuth.SendPasswordResetAsync(request.Email);

    // Log the attempt with user context if available
    await auditService.LogEventAsync("password_reset", success ? "success" : "failure",
        tenantId: user?.TenantId, userId: user?.Id, ipAddress: ipAddress, userAgent: userAgent,
        details: new { email = request.Email, userExists = user != null });

    // Always return success to prevent email enumeration
    // Even if the email doesn't exist or Supabase fails
    return Results.Ok(new { message = "If an account with that email exists, a password reset link has been sent." });
})
.WithName("RequestPasswordReset")
.WithTags("Auth")
.AllowAnonymous()
.RequireRateLimiting("signup"); // Use signup rate limit - same strictness

// ==================== User Endpoints ====================

// GET /me - Get current user profile
app.MapGet("/me", async (
    ICurrentUserService currentUser,
    MnemoDbContext db) =>
{
    if (!currentUser.UserId.HasValue)
        return Results.Unauthorized();

    var user = await db.Users
        .Include(u => u.Tenant)
        .FirstOrDefaultAsync(u => u.Id == currentUser.UserId.Value);

    if (user == null)
        return Results.NotFound("User not found");

    return Results.Ok(new UserProfileDto(
        user.Id,
        user.Email,
        user.Name,
        user.Role,
        user.TenantId,
        user.Tenant.Name,
        user.CreatedAt));
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetCurrentUser")
.WithTags("Users")
.RequireRateLimiting("api");

// PATCH /me - Update current user profile
app.MapPatch("/me", async (
    UpdateProfileRequest request,
    ICurrentUserService currentUser,
    MnemoDbContext db) =>
{
    if (!currentUser.UserId.HasValue)
        return Results.Unauthorized();

    var user = await db.Users.FindAsync(currentUser.UserId.Value);
    if (user == null)
        return Results.NotFound("User not found");

    if (request.Name != null)
        user.Name = request.Name;

    user.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    var tenant = await db.Tenants.FindAsync(user.TenantId);

    return Results.Ok(new UserProfileDto(
        user.Id,
        user.Email,
        user.Name,
        user.Role,
        user.TenantId,
        tenant?.Name ?? "Unknown",
        user.CreatedAt));
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("UpdateCurrentUser")
.WithTags("Users")
.RequireRateLimiting("api");

// GET /tenant/users - List users in current tenant (admin only)
app.MapGet("/tenant/users", async (
    ICurrentUserService currentUser,
    MnemoDbContext db) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var users = await db.Users
        .Where(u => u.TenantId == currentUser.TenantId.Value)
        .OrderBy(u => u.CreatedAt)
        .Select(u => new TenantUserDto(
            u.Id,
            u.Email,
            u.Name,
            u.Role,
            u.CreatedAt))
        .ToListAsync();

    return Results.Ok(users);
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("ListTenantUsers")
.WithTags("Users")
.RequireRateLimiting("api");

// POST /tenant/users/invite - Invite a user to the tenant (admin only)
app.MapPost("/tenant/users/invite", async (
    InviteUserRequest request,
    ICurrentUserService currentUser,
    ISupabaseAuthService supabaseAuth,
    IAuditService auditService,
    MnemoDbContext db,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();

    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    // Validate email
    if (!IsValidEmail(request.Email))
    {
        await auditService.LogEventAsync("invite", "failure",
            tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "invalid_email" });
        return Results.BadRequest("Valid email is required");
    }

    // Validate role
    string[] allowedRoles = [UserRole.User, UserRole.Admin];
    if (!allowedRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
    {
        await auditService.LogEventAsync("invite", "failure",
            tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "invalid_role", role = request.Role });
        return Results.BadRequest($"Invalid role. Allowed: {string.Join(", ", allowedRoles)}");
    }

    // Check if user already exists in this tenant
    var existingUser = await db.Users
        .FirstOrDefaultAsync(u => u.Email == request.Email && u.TenantId == currentUser.TenantId.Value);

    if (existingUser != null)
    {
        await auditService.LogEventAsync("invite", "failure",
            tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "user_exists" });
        return Results.Conflict("User already exists in this tenant");
    }

    try
    {
        // Use Supabase Auth service to invite user
        var result = await supabaseAuth.InviteUserAsync(request.Email);

        if (!result.Success)
        {
            logger.LogWarning("Supabase invite failed: {Error}", result.Error);
            await auditService.LogEventAsync("invite", "failure",
                tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
                details: new { email = request.Email, reason = "supabase_error", error = result.Error });
            return Results.BadRequest(result.Error ?? "Failed to invite user");
        }

        // Pre-create the user record in our database
        var newUser = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = result.SupabaseUserId,
            Email = request.Email,
            Role = request.Role,
            TenantId = currentUser.TenantId.Value,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Users.Add(newUser);
        await db.SaveChangesAsync();

        logger.LogInformation("User {Email} invited to tenant {TenantId}", request.Email, currentUser.TenantId.Value);

        // Log successful invite
        await auditService.LogEventAsync("invite", "success",
            tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
            details: new { invitedEmail = request.Email, invitedUserId = newUser.Id, role = request.Role });

        return Results.Ok(new InviteUserResponse(
            "Invitation sent successfully",
            request.Email));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to invite user {Email}", request.Email);
        await auditService.LogEventAsync("invite", "failure",
            tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
            details: new { email = request.Email, reason = "exception", error = ex.Message });
        return Results.Problem("Failed to send invitation. Please try again.");
    }
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("InviteUser")
.WithTags("Users")
.RequireRateLimiting("invite");

// PATCH /tenant/users/{userId}/deactivate - Deactivate a user (admin only)
app.MapPatch("/tenant/users/{userId:guid}/deactivate", async (
    Guid userId,
    ICurrentUserService currentUser,
    IAuditService auditService,
    MnemoDbContext db,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();

    if (!currentUser.TenantId.HasValue || !currentUser.UserId.HasValue)
        return Results.Unauthorized();

    // Prevent self-deactivation
    if (userId == currentUser.UserId.Value)
    {
        await auditService.LogEventAsync("user_deactivate", "failure",
            tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
            details: new { targetUserId = userId, reason = "self_deactivation" });
        return Results.BadRequest("Cannot deactivate your own account");
    }

    var targetUser = await db.Users
        .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == currentUser.TenantId.Value);

    if (targetUser == null)
        return Results.NotFound("User not found");

    if (!targetUser.IsActive)
        return Results.BadRequest("User is already deactivated");

    targetUser.IsActive = false;
    targetUser.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    logger.LogInformation("User {UserId} deactivated by {AdminId}", userId, currentUser.UserId);

    await auditService.LogEventAsync("user_deactivate", "success",
        tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
        details: new { targetUserId = userId, targetEmail = targetUser.Email });

    return Results.Ok(new { message = "User deactivated successfully", userId = targetUser.Id });
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("DeactivateUser")
.WithTags("Users")
.RequireRateLimiting("api");

// PATCH /tenant/users/{userId}/reactivate - Reactivate a user (admin only)
app.MapPatch("/tenant/users/{userId:guid}/reactivate", async (
    Guid userId,
    ICurrentUserService currentUser,
    IAuditService auditService,
    MnemoDbContext db,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
    var userAgent = httpContext.Request.Headers.UserAgent.ToString();

    if (!currentUser.TenantId.HasValue || !currentUser.UserId.HasValue)
        return Results.Unauthorized();

    // Need to query without the IsActive filter to find deactivated users
    var targetUser = await db.Users
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == currentUser.TenantId.Value);

    if (targetUser == null)
        return Results.NotFound("User not found");

    if (targetUser.IsActive)
        return Results.BadRequest("User is already active");

    targetUser.IsActive = true;
    targetUser.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    logger.LogInformation("User {UserId} reactivated by {AdminId}", userId, currentUser.UserId);

    await auditService.LogEventAsync("user_reactivate", "success",
        tenantId: currentUser.TenantId, userId: currentUser.UserId, ipAddress: ipAddress, userAgent: userAgent,
        details: new { targetUserId = userId, targetEmail = targetUser.Email });

    return Results.Ok(new { message = "User reactivated successfully", userId = targetUser.Id });
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("ReactivateUser")
.WithTags("Users")
.RequireRateLimiting("api");

app.Run();

// Make Program accessible for integration tests
public partial class Program
{
    /// <summary>
    /// Validates email format using System.Net.Mail.MailAddress.
    /// More robust than simple Contains('@') check.
    /// </summary>
    internal static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the client IP address, checking X-Forwarded-For header for proxy/load balancer scenarios.
    /// </summary>
    internal static string GetClientIp(HttpContext context)
    {
        // Check X-Forwarded-For (first IP in chain is original client)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var ip = forwardedFor.Split(',').First().Trim();
            // Basic validation - should be a valid IP
            if (System.Net.IPAddress.TryParse(ip, out _))
                return ip;
        }
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
