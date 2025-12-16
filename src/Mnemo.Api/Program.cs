using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mnemo.Api.Authorization;
using Mnemo.Api.Services;
using Mnemo.Application.Configuration;
using Mnemo.Application.DTOs;
using Mnemo.Application.Services;
using Mnemo.Infrastructure.Persistence;

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

// Register CurrentUser service
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

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
            ClockSkew = TimeSpan.Zero
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

var app = builder.Build();

// Configure middleware pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck")
    .WithTags("Health");

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
.WithTags("Users");

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
.WithTags("Users");

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
.WithTags("Users");

// POST /tenant/users/invite - Invite a user to the tenant (admin only)
app.MapPost("/tenant/users/invite", async (
    InviteUserRequest request,
    ICurrentUserService currentUser,
    MnemoDbContext db,
    IConfiguration configuration,
    ILogger<Program> logger) =>
{
    if (!currentUser.TenantId.HasValue)
        return Results.Unauthorized();

    // Validate email
    if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        return Results.BadRequest("Valid email is required");

    // Check if user already exists in this tenant
    var existingUser = await db.Users
        .FirstOrDefaultAsync(u => u.Email == request.Email && u.TenantId == currentUser.TenantId.Value);

    if (existingUser != null)
        return Results.Conflict("User already exists in this tenant");

    try
    {
        var settings = configuration.GetSection(SupabaseSettings.SectionName).Get<SupabaseSettings>()!;

        // Use Supabase Admin API to invite user via HTTP
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Add("apikey", settings.ServiceRoleKey);
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {settings.ServiceRoleKey}");

        var inviteResponse = await httpClient.PostAsJsonAsync(
            $"{settings.Url}/auth/v1/invite",
            new { email = request.Email });

        if (inviteResponse.IsSuccessStatusCode)
        {
            var responseData = await inviteResponse.Content.ReadFromJsonAsync<SupabaseInviteResponse>();

            // Pre-create the user record in our database
            var newUser = new Mnemo.Domain.Entities.User
            {
                Id = Guid.NewGuid(),
                SupabaseUserId = responseData?.Id,
                Email = request.Email,
                Role = request.Role,
                TenantId = currentUser.TenantId.Value,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Users.Add(newUser);
            await db.SaveChangesAsync();

            logger.LogInformation("User {Email} invited to tenant {TenantId}", request.Email, currentUser.TenantId.Value);

            return Results.Ok(new InviteUserResponse(
                "Invitation sent successfully",
                request.Email));
        }

        var errorContent = await inviteResponse.Content.ReadAsStringAsync();
        logger.LogWarning("Supabase invite failed: {StatusCode} - {Content}", inviteResponse.StatusCode, errorContent);
        return Results.BadRequest("Failed to invite user. Check if email is valid.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to invite user {Email}", request.Email);
        return Results.Problem("Failed to send invitation. Please try again.");
    }
})
.RequireAuthorization(AuthorizationPolicies.RequireAdmin)
.WithName("InviteUser")
.WithTags("Users");

app.Run();

// Response model for Supabase invite
record SupabaseInviteResponse(string? Id, string? Email);

// Make Program accessible for integration tests
public partial class Program { }
