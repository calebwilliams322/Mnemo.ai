using System.Net.Http.Json;
using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mnemo.Api.Authorization;
using Mnemo.Api.Hubs;
using Mnemo.Api.Services;
using Mnemo.Application.Configuration;
using Mnemo.Application.DTOs;
using Mnemo.Application.Services;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;
using Mnemo.Infrastructure.Persistence;
using Mnemo.Infrastructure.Services;
using Mnemo.Api.EventHandlers;
using Mnemo.Domain.Events;
using Mnemo.Infrastructure.EventHandlers;
using Microsoft.AspNetCore.Mvc;
using Mnemo.Extraction.Interfaces;
using Mnemo.Extraction.Services;
using Mnemo.Extraction.DependencyInjection;
using System.Text.Json;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Bind configuration
var supabaseSettings = builder.Configuration
    .GetSection(SupabaseSettings.SectionName)
    .Get<SupabaseSettings>() ?? throw new InvalidOperationException("Supabase configuration is required");

// Add services
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();

// Configure CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000", "http://127.0.0.1:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Register DbContext with pgvector support
// NpgsqlDataSourceBuilder is required for Npgsql 8+ to properly handle Vector type parameters in queries
// UseVector() must be called on BOTH the data source builder AND the EF Core options
var connectionString = builder.Configuration.GetValue<string>("Database:ConnectionString")
    ?? throw new InvalidOperationException("Database connection string is required");
var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<MnemoDbContext>(options =>
    options.UseNpgsql(dataSource, npgsqlOptions => npgsqlOptions.UseVector()));

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

// Register Storage service (Supabase Storage)
builder.Services.AddScoped<IStorageService, SupabaseStorageService>();

// Configure OpenAI settings for embeddings
builder.Services.Configure<OpenAISettings>(builder.Configuration.GetSection("OpenAI"));

// Configure Claude settings for extraction
builder.Services.Configure<ClaudeExtractionSettings>(builder.Configuration.GetSection("Claude"));

// Configure Claude Chat settings (uses same API key as extraction)
builder.Services.Configure<ClaudeChatSettings>(builder.Configuration.GetSection("Claude"));

// Configure Chat settings for RAG
builder.Services.Configure<ChatSettings>(builder.Configuration.GetSection("Chat"));

// Register all extraction services (PDF, Claude, embeddings)
builder.Services.AddExtractionServices();

// Register ClaudeChatService for streaming chat
builder.Services.AddScoped<IClaudeChatService, ClaudeChatService>();

// Register SemanticSearchService for pgvector similarity search
builder.Services.AddScoped<ISemanticSearchService, SemanticSearchService>();

// Register ChatService for RAG-powered conversations
builder.Services.AddScoped<IChatService, ChatService>();

// Register ExtractionPipeline (orchestrates Phase 7 services)
builder.Services.AddScoped<IExtractionPipeline, ExtractionPipeline>();

// Register Event Publisher and Background Job Service
builder.Services.AddScoped<IEventPublisher, EventPublisher>();
builder.Services.AddScoped<IBackgroundJobService, HangfireJobService>();

// Register Document Processing Service (called by Hangfire jobs)
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();

// Configure Hangfire with PostgreSQL storage (NOT in-memory)
builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));

// Add Hangfire server with 2 worker threads for parallel processing
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = 2;
    options.Queues = new[] { "default", "extraction" };
});

// Configure SignalR for real-time notifications
builder.Services.AddSignalR();

// Register SignalR notification service
builder.Services.AddScoped<INotificationService, SignalRNotificationService>();

// Register document event handlers for SignalR notifications
builder.Services.AddScoped<IEventHandler<DocumentUploadedEvent>, DocumentUploadedEventHandler>();
builder.Services.AddScoped<IEventHandler<DocumentProcessingStartedEvent>, DocumentProcessingStartedEventHandler>();
builder.Services.AddScoped<IEventHandler<DocumentProgressEvent>, DocumentProgressEventHandler>();
builder.Services.AddScoped<IEventHandler<DocumentProcessedEvent>, DocumentProcessedEventHandler>();

// Register Webhook Service
builder.Services.AddScoped<IWebhookService, WebhookService>();

// Register webhook event handlers (subscribe to same events as SignalR)
builder.Services.AddScoped<IEventHandler<DocumentUploadedEvent>, WebhookDocumentUploadedHandler>();
builder.Services.AddScoped<IEventHandler<DocumentProcessingStartedEvent>, WebhookDocumentProcessingStartedHandler>();
builder.Services.AddScoped<IEventHandler<DocumentProcessedEvent>, WebhookDocumentProcessedHandler>();

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
            // Handle SignalR token from query string (WebSocket/SSE don't support headers)
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                logger.LogInformation("OnMessageReceived: Path={Path}, HasToken={HasToken}", path, !string.IsNullOrEmpty(accessToken));

                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    logger.LogInformation("Setting token from query string for SignalR");
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
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

    // Stricter limit for expensive chat operations - 20 requests per user per minute
    // Uses sliding window for smoother rate limiting (better for bursty chat usage)
    options.AddPolicy("chat", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.User.Identity?.Name ?? GetClientIp(context),
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4, // 15-second segments for smoother limiting
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 2 // Allow small queue for burst tolerance
            }));
});

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseWebSockets();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Map SignalR hub for real-time notifications
app.MapHub<NotificationHub>("/hubs/notifications");

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

// ==================== Document Endpoints ====================

// POST /documents/upload - Upload a single document
app.MapPost("/documents/upload", async (
    IFormFile file,
    ICurrentUserService currentUser,
    IStorageService storageService,
    IBackgroundJobService jobService,
    IEventPublisher eventPublisher,
    MnemoDbContext db,
    ILogger<Program> logger) =>
{
    if (!currentUser.TenantId.HasValue || !currentUser.UserId.HasValue)
        return Results.Unauthorized();

    // Validate file
    if (file == null || file.Length == 0)
        return Results.BadRequest("No file provided");

    // Only accept PDFs for now
    if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
        !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest("Only PDF files are accepted");
    }

    var documentId = Guid.NewGuid();
    var tenantId = currentUser.TenantId.Value;

    try
    {
        // Upload to Supabase Storage
        await using var stream = file.OpenReadStream();
        var storagePath = await storageService.UploadAsync(
            tenantId,
            documentId,
            file.FileName,
            stream,
            file.ContentType);

        // Create document record
        var document = new Document
        {
            Id = documentId,
            TenantId = tenantId,
            FileName = file.FileName,
            StoragePath = storagePath,
            FileSizeBytes = file.Length,
            ContentType = file.ContentType,
            ProcessingStatus = "pending",
            UploadedByUserId = currentUser.UserId,
            UploadedAt = DateTime.UtcNow
        };

        db.Documents.Add(document);
        await db.SaveChangesAsync();

        // Publish upload event for SignalR notification
        await eventPublisher.PublishAsync(new DocumentUploadedEvent
        {
            DocumentId = documentId,
            TenantId = tenantId,
            FileName = file.FileName,
            StoragePath = storagePath
        });

        // Queue background job for processing
        jobService.Enqueue<IDocumentProcessingService>(
            svc => svc.ProcessDocumentAsync(documentId, tenantId));

        logger.LogInformation(
            "Document uploaded: {DocumentId} ({FileName}) by user {UserId}",
            documentId, file.FileName, currentUser.UserId);

        return Results.Created($"/documents/{documentId}", new DocumentUploadResponse(
            documentId,
            file.FileName,
            "pending",
            document.UploadedAt));
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to upload document: {FileName}", file.FileName);
        return Results.Problem("Failed to upload document");
    }
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("UploadDocument")
.WithTags("Documents")
.RequireRateLimiting("api")
.DisableAntiforgery(); // Required for file uploads

// POST /documents/upload/batch - Upload multiple documents at once
app.MapPost("/documents/upload/batch", async (
    IFormFileCollection files,
    ICurrentUserService currentUser,
    IStorageService storageService,
    IBackgroundJobService jobService,
    IEventPublisher eventPublisher,
    MnemoDbContext db,
    ILogger<Program> logger) =>
{
    if (!currentUser.TenantId.HasValue || !currentUser.UserId.HasValue)
        return Results.Unauthorized();

    if (files == null || files.Count == 0)
        return Results.BadRequest("No files provided");

    // Limit batch size
    const int maxBatchSize = 5;
    if (files.Count > maxBatchSize)
        return Results.BadRequest($"Maximum {maxBatchSize} files per batch");

    var tenantId = currentUser.TenantId.Value;
    var uploadedDocs = new List<DocumentUploadResponse>();
    var errors = new List<string>();

    foreach (var file in files)
    {
        // Skip non-PDF files
        if (!file.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"Skipped '{file.FileName}': Only PDF files are accepted");
            continue;
        }

        var documentId = Guid.NewGuid();

        try
        {
            await using var stream = file.OpenReadStream();
            var storagePath = await storageService.UploadAsync(
                tenantId,
                documentId,
                file.FileName,
                stream,
                file.ContentType);

            var document = new Document
            {
                Id = documentId,
                TenantId = tenantId,
                FileName = file.FileName,
                StoragePath = storagePath,
                FileSizeBytes = file.Length,
                ContentType = file.ContentType,
                ProcessingStatus = "pending",
                UploadedByUserId = currentUser.UserId,
                UploadedAt = DateTime.UtcNow
            };

            db.Documents.Add(document);
            await db.SaveChangesAsync();

            // Publish upload event
            await eventPublisher.PublishAsync(new DocumentUploadedEvent
            {
                DocumentId = documentId,
                TenantId = tenantId,
                FileName = file.FileName,
                StoragePath = storagePath
            });

            // Queue processing job
            jobService.Enqueue<IDocumentProcessingService>(
                svc => svc.ProcessDocumentAsync(documentId, tenantId));

            uploadedDocs.Add(new DocumentUploadResponse(
                documentId,
                file.FileName,
                "pending",
                document.UploadedAt));

            logger.LogInformation(
                "Document uploaded (batch): {DocumentId} ({FileName})",
                documentId, file.FileName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload document in batch: {FileName}", file.FileName);
            errors.Add($"Failed to upload '{file.FileName}': {ex.Message}");
        }
    }

    if (uploadedDocs.Count == 0 && errors.Count > 0)
        return Results.BadRequest(new { errors });

    return Results.Ok(new BatchUploadResponse(
        uploadedDocs.Count,
        uploadedDocs));
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("BatchUploadDocuments")
.WithTags("Documents")
.RequireRateLimiting("api")
.DisableAntiforgery();

// GET /documents - List documents for current tenant
app.MapGet("/documents", async (
    ICurrentUserService currentUser,
    MnemoDbContext db,
    int page = 1,
    int pageSize = 20,
    string? status = null) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var query = db.Documents
        .Where(d => d.TenantId == currentUser.TenantId.Value)
        .AsQueryable();

    if (!string.IsNullOrEmpty(status))
        query = query.Where(d => d.ProcessingStatus == status);

    var totalCount = await query.CountAsync();

    var documents = await query
        .OrderByDescending(d => d.UploadedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(d => new DocumentSummaryDto(
            d.Id,
            d.FileName,
            d.ProcessingStatus,
            d.DocumentType,
            d.UploadedAt))
        .ToListAsync();

    return Results.Ok(new PaginatedResponse<DocumentSummaryDto>
    {
        Items = documents,
        TotalCount = totalCount,
        Page = page,
        PageSize = pageSize
    });
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("ListDocuments")
.WithTags("Documents")
.RequireRateLimiting("api");

// GET /documents/{id} - Get document details
app.MapGet("/documents/{id:guid}", async (
    Guid id,
    ICurrentUserService currentUser,
    MnemoDbContext db) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var document = await db.Documents
        .Where(d => d.Id == id && d.TenantId == currentUser.TenantId.Value)
        .Select(d => new DocumentDto(
            d.Id,
            d.FileName,
            d.ContentType,
            d.FileSizeBytes,
            d.PageCount,
            d.DocumentType,
            d.ProcessingStatus,
            d.ProcessingError,
            d.ProcessedAt,
            d.UploadedAt,
            d.UploadedByUserId,
            d.SubmissionGroupId))
        .FirstOrDefaultAsync();

    if (document == null)
        return Results.NotFound("Document not found");

    return Results.Ok(document);
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetDocument")
.WithTags("Documents")
.RequireRateLimiting("api");

// GET /documents/{id}/download - Get signed URL for document download
app.MapGet("/documents/{id:guid}/download", async (
    Guid id,
    ICurrentUserService currentUser,
    IStorageService storageService,
    MnemoDbContext db) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var document = await db.Documents
        .Where(d => d.Id == id && d.TenantId == currentUser.TenantId.Value)
        .FirstOrDefaultAsync();

    if (document == null)
        return Results.NotFound("Document not found");

    // Generate signed URL valid for 1 hour
    var signedUrl = await storageService.GetSignedUrlAsync(
        document.StoragePath,
        TimeSpan.FromHours(1));

    return Results.Ok(new
    {
        downloadUrl = signedUrl,
        fileName = document.FileName,
        expiresIn = "1 hour"
    });
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetDocumentDownloadUrl")
.WithTags("Documents")
.RequireRateLimiting("api");

// DELETE /documents/{id} - Delete a document
app.MapDelete("/documents/{id:guid}", async (
    Guid id,
    ICurrentUserService currentUser,
    IStorageService storageService,
    MnemoDbContext db,
    ILogger<Program> logger) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var document = await db.Documents
        .Where(d => d.Id == id && d.TenantId == currentUser.TenantId.Value)
        .FirstOrDefaultAsync();

    if (document == null)
        return Results.NotFound("Document not found");

    try
    {
        // Delete from storage
        await storageService.DeleteAsync(document.StoragePath);

        // Delete from database
        db.Documents.Remove(document);
        await db.SaveChangesAsync();

        logger.LogInformation(
            "Document deleted: {DocumentId} by user {UserId}",
            id, currentUser.UserId);

        return Results.NoContent();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to delete document: {DocumentId}", id);
        return Results.Problem("Failed to delete document");
    }
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("DeleteDocument")
.WithTags("Documents")
.RequireRateLimiting("api");

// ==================== Webhook Endpoints ====================

// POST /webhooks - Create a new webhook
app.MapPost("/webhooks", async (
    CreateWebhookRequest request,
    ICurrentUserService currentUser,
    MnemoDbContext db,
    ILogger<Program> logger) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    // Validate URL
    if (string.IsNullOrWhiteSpace(request.Url) || !Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        return Results.BadRequest("Valid URL is required");

    if (uri.Scheme != "https" && uri.Scheme != "http")
        return Results.BadRequest("URL must use HTTP or HTTPS");

    // Validate events
    if (request.Events == null || request.Events.Count == 0)
        return Results.BadRequest("At least one event type is required");

    foreach (var evt in request.Events)
    {
        if (!WebhookEventTypes.IsValid(evt) && evt != "*")
            return Results.BadRequest($"Invalid event type: {evt}. Valid types: {string.Join(", ", WebhookEventTypes.All)}");
    }

    var webhook = new Webhook
    {
        Id = Guid.NewGuid(),
        TenantId = currentUser.TenantId.Value,
        Url = request.Url,
        Events = JsonSerializer.Serialize(request.Events),
        Secret = request.Secret,
        IsActive = true,
        CreatedAt = DateTime.UtcNow
    };

    db.Webhooks.Add(webhook);
    await db.SaveChangesAsync();

    logger.LogInformation(
        "Webhook created: {WebhookId} for tenant {TenantId}",
        webhook.Id, currentUser.TenantId);

    return Results.Created($"/webhooks/{webhook.Id}", new WebhookDto(
        webhook.Id,
        webhook.Url,
        request.Events,
        webhook.IsActive,
        webhook.ConsecutiveFailures,
        webhook.CreatedAt,
        webhook.UpdatedAt));
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("CreateWebhook")
.WithTags("Webhooks")
.RequireRateLimiting("api");

// GET /webhooks - List webhooks for tenant
app.MapGet("/webhooks", async (
    ICurrentUserService currentUser,
    MnemoDbContext db) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var webhooks = await db.Webhooks
        .Where(w => w.TenantId == currentUser.TenantId.Value)
        .OrderByDescending(w => w.CreatedAt)
        .ToListAsync();

    var result = webhooks.Select(w => new WebhookDto(
        w.Id,
        w.Url,
        JsonSerializer.Deserialize<List<string>>(w.Events) ?? [],
        w.IsActive,
        w.ConsecutiveFailures,
        w.CreatedAt,
        w.UpdatedAt));

    return Results.Ok(result);
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("ListWebhooks")
.WithTags("Webhooks")
.RequireRateLimiting("api");

// GET /webhooks/{id} - Get webhook details
app.MapGet("/webhooks/{id:guid}", async (
    Guid id,
    ICurrentUserService currentUser,
    MnemoDbContext db) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var webhook = await db.Webhooks
        .Where(w => w.Id == id && w.TenantId == currentUser.TenantId.Value)
        .FirstOrDefaultAsync();

    if (webhook == null)
        return Results.NotFound("Webhook not found");

    return Results.Ok(new WebhookDto(
        webhook.Id,
        webhook.Url,
        JsonSerializer.Deserialize<List<string>>(webhook.Events) ?? new List<string>(),
        webhook.IsActive,
        webhook.ConsecutiveFailures,
        webhook.CreatedAt,
        webhook.UpdatedAt));
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("GetWebhook")
.WithTags("Webhooks")
.RequireRateLimiting("api");

// PATCH /webhooks/{id} - Update webhook
app.MapPatch("/webhooks/{id:guid}", async (
    Guid id,
    UpdateWebhookRequest request,
    ICurrentUserService currentUser,
    MnemoDbContext db,
    ILogger<Program> logger) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var webhook = await db.Webhooks
        .Where(w => w.Id == id && w.TenantId == currentUser.TenantId.Value)
        .FirstOrDefaultAsync();

    if (webhook == null)
        return Results.NotFound("Webhook not found");

    if (request.Url != null)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            return Results.BadRequest("Valid URL is required");
        webhook.Url = request.Url;
    }

    if (request.Events != null)
    {
        foreach (var evt in request.Events)
        {
            if (!WebhookEventTypes.IsValid(evt) && evt != "*")
                return Results.BadRequest($"Invalid event type: {evt}");
        }
        webhook.Events = JsonSerializer.Serialize(request.Events);
    }

    if (request.Secret != null)
        webhook.Secret = request.Secret;

    if (request.IsActive.HasValue)
    {
        webhook.IsActive = request.IsActive.Value;
        // Reset failure count when re-enabling
        if (request.IsActive.Value)
            webhook.ConsecutiveFailures = 0;
    }

    webhook.UpdatedAt = DateTime.UtcNow;
    await db.SaveChangesAsync();

    logger.LogInformation("Webhook updated: {WebhookId}", webhook.Id);

    return Results.Ok(new WebhookDto(
        webhook.Id,
        webhook.Url,
        JsonSerializer.Deserialize<List<string>>(webhook.Events) ?? new List<string>(),
        webhook.IsActive,
        webhook.ConsecutiveFailures,
        webhook.CreatedAt,
        webhook.UpdatedAt));
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("UpdateWebhook")
.WithTags("Webhooks")
.RequireRateLimiting("api");

// DELETE /webhooks/{id} - Delete webhook
app.MapDelete("/webhooks/{id:guid}", async (
    Guid id,
    ICurrentUserService currentUser,
    MnemoDbContext db,
    ILogger<Program> logger) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var webhook = await db.Webhooks
        .Where(w => w.Id == id && w.TenantId == currentUser.TenantId.Value)
        .FirstOrDefaultAsync();

    if (webhook == null)
        return Results.NotFound("Webhook not found");

    db.Webhooks.Remove(webhook);
    await db.SaveChangesAsync();

    logger.LogInformation("Webhook deleted: {WebhookId}", webhook.Id);

    return Results.NoContent();
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("DeleteWebhook")
.WithTags("Webhooks")
.RequireRateLimiting("api");

// GET /webhooks/{id}/deliveries - Get delivery history
app.MapGet("/webhooks/{id:guid}/deliveries", async (
    Guid id,
    ICurrentUserService currentUser,
    MnemoDbContext db,
    int page = 1,
    int pageSize = 20) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    // Verify webhook belongs to tenant
    var webhookExists = await db.Webhooks
        .AnyAsync(w => w.Id == id && w.TenantId == currentUser.TenantId.Value);

    if (!webhookExists)
        return Results.NotFound("Webhook not found");

    var totalCount = await db.WebhookDeliveries
        .CountAsync(d => d.WebhookId == id);

    var deliveries = await db.WebhookDeliveries
        .Where(d => d.WebhookId == id)
        .OrderByDescending(d => d.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(d => new WebhookDeliveryDto(
            d.Id,
            d.WebhookId,
            d.Event,
            d.Status,
            d.ResponseStatusCode,
            d.ErrorMessage,
            d.AttemptCount,
            d.CreatedAt,
            d.DeliveredAt))
        .ToListAsync();

    return Results.Ok(new
    {
        data = deliveries,
        pagination = new
        {
            page,
            pageSize,
            totalCount,
            totalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        }
    });
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("GetWebhookDeliveries")
.WithTags("Webhooks")
.RequireRateLimiting("api");

// ============================================================================
// Policy Endpoints
// ============================================================================

// GET /policies - List policies with filters
app.MapGet("/policies", async (
    ICurrentUserService currentUser,
    MnemoDbContext db,
    [FromQuery] string? insuredName,
    [FromQuery] string? carrierName,
    [FromQuery] string? status,
    [FromQuery] DateOnly? effectiveAfter,
    [FromQuery] DateOnly? effectiveBefore,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var query = db.Policies
        .Include(p => p.Coverages)
        .Where(p => p.TenantId == currentUser.TenantId.Value);

    // Apply filters
    if (!string.IsNullOrWhiteSpace(insuredName))
        query = query.Where(p => p.InsuredName != null &&
            p.InsuredName.ToLower().Contains(insuredName.ToLower()));

    if (!string.IsNullOrWhiteSpace(carrierName))
        query = query.Where(p => p.CarrierName != null &&
            p.CarrierName.ToLower().Contains(carrierName.ToLower()));

    if (!string.IsNullOrWhiteSpace(status))
        query = query.Where(p => p.PolicyStatus == status);

    if (effectiveAfter.HasValue)
        query = query.Where(p => p.EffectiveDate >= effectiveAfter.Value);

    if (effectiveBefore.HasValue)
        query = query.Where(p => p.EffectiveDate <= effectiveBefore.Value);

    var totalCount = await query.CountAsync();

    var policies = await query
        .OrderByDescending(p => p.CreatedAt)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(p => new PolicyListItemDto
        {
            Id = p.Id,
            PolicyNumber = p.PolicyNumber,
            InsuredName = p.InsuredName,
            CarrierName = p.CarrierName,
            EffectiveDate = p.EffectiveDate,
            ExpirationDate = p.ExpirationDate,
            PolicyStatus = p.PolicyStatus,
            TotalPremium = p.TotalPremium,
            ExtractionConfidence = p.ExtractionConfidence,
            CoverageCount = p.Coverages.Count,
            CreatedAt = p.CreatedAt
        })
        .ToListAsync();

    return Results.Ok(new PaginatedResponse<PolicyListItemDto>
    {
        Items = policies,
        TotalCount = totalCount,
        Page = page,
        PageSize = pageSize
    });
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetPolicies")
.WithTags("Policies")
.RequireRateLimiting("api");

// GET /policies/{id} - Get policy details with coverages
app.MapGet("/policies/{id:guid}", async (
    Guid id,
    ICurrentUserService currentUser,
    MnemoDbContext db) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var policy = await db.Policies
        .Include(p => p.Coverages)
        .Include(p => p.SourceDocument)
        .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == currentUser.TenantId.Value);

    if (policy == null)
        return Results.NotFound("Policy not found");

    var dto = new PolicyDetailDto
    {
        Id = policy.Id,
        SourceDocumentId = policy.SourceDocumentId,
        SourceDocumentName = policy.SourceDocument?.FileName,
        PolicyNumber = policy.PolicyNumber,
        QuoteNumber = policy.QuoteNumber,
        EffectiveDate = policy.EffectiveDate,
        ExpirationDate = policy.ExpirationDate,
        QuoteExpirationDate = policy.QuoteExpirationDate,
        CarrierName = policy.CarrierName,
        CarrierNaic = policy.CarrierNaic,
        InsuredName = policy.InsuredName,
        InsuredAddressLine1 = policy.InsuredAddressLine1,
        InsuredAddressLine2 = policy.InsuredAddressLine2,
        InsuredCity = policy.InsuredCity,
        InsuredState = policy.InsuredState,
        InsuredZip = policy.InsuredZip,
        TotalPremium = policy.TotalPremium,
        PolicyStatus = policy.PolicyStatus,
        ExtractionConfidence = policy.ExtractionConfidence,
        CreatedAt = policy.CreatedAt,
        UpdatedAt = policy.UpdatedAt,
        Coverages = policy.Coverages.Select(c => new CoverageDto
        {
            Id = c.Id,
            CoverageType = c.CoverageType,
            CoverageSubtype = c.CoverageSubtype,
            EachOccurrenceLimit = c.EachOccurrenceLimit,
            AggregateLimit = c.AggregateLimit,
            Deductible = c.Deductible,
            Premium = c.Premium,
            IsOccurrenceForm = c.IsOccurrenceForm,
            IsClaimsMade = c.IsClaimsMade,
            RetroactiveDate = c.RetroactiveDate,
            ExtractionConfidence = c.ExtractionConfidence,
            Details = ParseCoverageDetails(c.Details)
        }).ToList()
    };

    return Results.Ok(dto);
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetPolicy")
.WithTags("Policies")
.RequireRateLimiting("api");

// GET /policies/{id}/summary - Get AI-generated policy summary
app.MapGet("/policies/{id:guid}/summary", async (
    Guid id,
    ICurrentUserService currentUser,
    MnemoDbContext db,
    IClaudeExtractionService claudeService) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var policy = await db.Policies
        .Include(p => p.Coverages)
        .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == currentUser.TenantId.Value);

    if (policy == null)
        return Results.NotFound("Policy not found");

    // Build context for summary generation
    var coverageSummary = string.Join("\n", policy.Coverages.Select(c =>
        $"- {c.CoverageType}: Occurrence ${c.EachOccurrenceLimit:N0}, Aggregate ${c.AggregateLimit:N0}, Deductible ${c.Deductible:N0}"));

    var prompt = $$"""
        Summarize this insurance policy for a broker:

        Policy: {{policy.PolicyNumber ?? "N/A"}}
        Insured: {{policy.InsuredName}}
        Carrier: {{policy.CarrierName}}
        Effective: {{policy.EffectiveDate}} to {{policy.ExpirationDate}}
        Premium: ${{policy.TotalPremium:N0}}

        Coverages:
        {{coverageSummary}}

        Provide:
        1. A 2-3 sentence summary
        2. 3-5 key points
        3. Any notable exclusions or gaps
        4. Recommendations

        Return as JSON:
        {
            "summary": "...",
            "key_points": ["...", "..."],
            "notable_exclusions": ["...", "..."],
            "recommendations": ["...", "..."]
        }
        """;

    try
    {
        var response = await claudeService.ExtractAsync<PolicySummaryResponse>(
            "You are an insurance policy analyst. Provide clear, actionable summaries.",
            prompt);

        if (!response.Success || response.Result == null)
        {
            return Results.Problem($"Failed to generate summary: {response.Error ?? "Unknown error"}");
        }

        return Results.Ok(new PolicySummaryDto
        {
            PolicyId = policy.Id,
            Summary = response.Result.Summary ?? "Unable to generate summary",
            KeyPoints = response.Result.KeyPoints ?? [],
            NotableExclusions = response.Result.NotableExclusions ?? [],
            Recommendations = response.Result.Recommendations ?? []
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to generate summary: {ex.Message}");
    }
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetPolicySummary")
.WithTags("Policies")
.RequireRateLimiting("api");

// GET /documents/{id}/extraction-status - Get extraction status
app.MapGet("/documents/{id:guid}/extraction-status", async (
    Guid id,
    ICurrentUserService currentUser,
    MnemoDbContext db) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var document = await db.Documents
        .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == currentUser.TenantId.Value);

    if (document == null)
        return Results.NotFound("Document not found");

    var policy = await db.Policies
        .Include(p => p.Coverages)
        .FirstOrDefaultAsync(p => p.SourceDocumentId == id);

    return Results.Ok(new ExtractionStatusDto
    {
        DocumentId = id,
        Status = document.ProcessingStatus,
        Error = document.ProcessingError,
        ProcessedAt = document.ProcessedAt,
        PolicyId = policy?.Id,
        PolicyNumber = policy?.PolicyNumber,
        CoveragesExtracted = policy?.Coverages.Count ?? 0,
        ExtractionConfidence = policy?.ExtractionConfidence
    });
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetExtractionStatus")
.WithTags("Documents")
.RequireRateLimiting("api");

// POST /documents/{id}/reprocess - Re-run extraction
app.MapPost("/documents/{id:guid}/reprocess", async (
    Guid id,
    ICurrentUserService currentUser,
    MnemoDbContext db,
    IBackgroundJobService jobService) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    var document = await db.Documents
        .FirstOrDefaultAsync(d => d.Id == id && d.TenantId == currentUser.TenantId.Value);

    if (document == null)
        return Results.NotFound("Document not found");

    // Delete existing policies for this document (cascade deletes coverages)
    var existingPolicies = await db.Policies
        .Where(p => p.SourceDocumentId == id)
        .ToListAsync();

    if (existingPolicies.Count > 0)
    {
        db.Policies.RemoveRange(existingPolicies);
    }

    // Delete existing chunks
    var existingChunks = await db.DocumentChunks
        .Where(c => c.DocumentId == id)
        .ToListAsync();

    if (existingChunks.Count > 0)
    {
        db.DocumentChunks.RemoveRange(existingChunks);
    }

    // Reset document status
    document.ProcessingStatus = "pending";
    document.ProcessingError = null;
    document.ProcessedAt = null;
    await db.SaveChangesAsync();

    // Queue reprocessing
    jobService.Enqueue<IDocumentProcessingService>(
        svc => svc.ProcessDocumentAsync(id, currentUser.TenantId.Value));

    return Results.Accepted(value: new { documentId = id, status = "reprocessing" });
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("ReprocessDocument")
.WithTags("Documents")
.RequireRateLimiting("api");

// ============================================================================
// Chat Endpoints (RAG-powered policy Q&A)
// ============================================================================

// POST /conversations - Create a new conversation
app.MapPost("/conversations", async (
    CreateConversationRequest request,
    IChatService chatService) =>
{
    var conversation = await chatService.CreateConversationAsync(request);

    return Results.Created($"/conversations/{conversation.Id}", new
    {
        id = conversation.Id,
        title = conversation.Title,
        policyIds = JsonSerializer.Deserialize<List<Guid>>(conversation.PolicyIds),
        documentIds = JsonSerializer.Deserialize<List<Guid>>(conversation.DocumentIds),
        createdAt = conversation.CreatedAt
    });
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("CreateConversation")
.WithTags("Chat")
.RequireRateLimiting("chat");

// GET /conversations - List user's conversations
app.MapGet("/conversations", async (
    IChatService chatService) =>
{
    var conversations = await chatService.ListConversationsAsync();
    return Results.Ok(conversations);
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("ListConversations")
.WithTags("Chat")
.RequireRateLimiting("api");

// GET /conversations/{id} - Get conversation with message history
app.MapGet("/conversations/{id:guid}", async (
    Guid id,
    IChatService chatService) =>
{
    var conversation = await chatService.GetConversationAsync(id);
    if (conversation == null)
        return Results.NotFound("Conversation not found");

    return Results.Ok(conversation);
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetConversation")
.WithTags("Chat")
.RequireRateLimiting("api");

// POST /conversations/{id}/messages - Send message (streaming via SSE)
app.MapPost("/conversations/{id:guid}/messages", async (
    Guid id,
    SendMessageRequest request,
    IChatService chatService,
    HttpContext httpContext,
    CancellationToken ct) =>
{
    httpContext.Response.ContentType = "text/event-stream";
    httpContext.Response.Headers.CacheControl = "no-cache";
    httpContext.Response.Headers.Connection = "keep-alive";

    try
    {
        await foreach (var evt in chatService.SendMessageAsync(id, request.Content, ct))
        {
            var json = JsonSerializer.Serialize(evt);
            await httpContext.Response.WriteAsync($"data: {json}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        }

        await httpContext.Response.WriteAsync("data: [DONE]\n\n", ct);
    }
    catch (OperationCanceledException)
    {
        // Client disconnected, this is expected
    }
    catch (Exception ex)
    {
        var errorEvent = JsonSerializer.Serialize(new { type = "error", error = ex.Message });
        await httpContext.Response.WriteAsync($"data: {errorEvent}\n\n", ct);
    }
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("SendMessage")
.WithTags("Chat")
.RequireRateLimiting("chat");

// GET /conversations/{id}/messages - Get message history
app.MapGet("/conversations/{id:guid}/messages", async (
    Guid id,
    int? limit,
    IChatService chatService) =>
{
    var conversation = await chatService.GetConversationAsync(id);
    if (conversation == null)
        return Results.NotFound("Conversation not found");

    var messages = conversation.Messages
        .OrderBy(m => m.CreatedAt)
        .TakeLast(limit ?? 50)
        .Select(m => new
        {
            id = m.Id,
            role = m.Role,
            content = m.Content,
            citedChunkIds = m.CitedChunkIds,
            promptTokens = m.PromptTokens,
            completionTokens = m.CompletionTokens,
            createdAt = m.CreatedAt
        });

    return Results.Ok(messages);
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("GetMessages")
.WithTags("Chat")
.RequireRateLimiting("api");

// DELETE /conversations/{id} - Delete conversation
app.MapDelete("/conversations/{id:guid}", async (
    Guid id,
    IChatService chatService) =>
{
    var deleted = await chatService.DeleteConversationAsync(id);
    if (!deleted)
        return Results.NotFound("Conversation not found");

    return Results.NoContent();
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("DeleteConversation")
.WithTags("Chat")
.RequireRateLimiting("api");

// PATCH /conversations/{id} - Update conversation (rename)
app.MapPatch("/conversations/{id:guid}", async (
    Guid id,
    UpdateConversationRequest request,
    IChatService chatService) =>
{
    var updated = await chatService.UpdateConversationAsync(id, request);
    if (!updated)
        return Results.NotFound("Conversation not found");

    return Results.NoContent();
})
.RequireAuthorization(AuthorizationPolicies.RequireTenant)
.WithName("UpdateConversation")
.WithTags("Chat")
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

    /// <summary>
    /// Parse coverage JSONB details string to dictionary.
    /// </summary>
    internal static Dictionary<string, JsonElement>? ParseCoverageDetails(string? detailsJson)
    {
        if (string.IsNullOrWhiteSpace(detailsJson) || detailsJson == "{}")
            return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(detailsJson);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Response model for policy summary generation.
/// </summary>
internal record PolicySummaryResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("key_points")]
    public List<string>? KeyPoints { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("notable_exclusions")]
    public List<string>? NotableExclusions { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("recommendations")]
    public List<string>? Recommendations { get; init; }
}
