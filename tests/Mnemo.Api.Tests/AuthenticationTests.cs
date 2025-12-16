using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Mnemo.Application.DTOs;
using Mnemo.Domain.Entities;
using Mnemo.Domain.Enums;
using Mnemo.Infrastructure.Persistence;

namespace Mnemo.Api.Tests;

public class AuthenticationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private Tenant? _testTenant;
    private User? _testUser;
    private User? _adminUser;

    public AuthenticationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Set up test data in the real database
        (_testTenant, _testUser, _adminUser) = await _factory.SetupTestDataAsync();
    }

    public async Task DisposeAsync()
    {
        // Clean up test data
        await _factory.CleanupTestDataAsync(_testTenant?.Id);
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk_WithoutAuthentication()
    {
        var response = await _client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetMe_WithoutToken_Returns401()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithInvalidToken_Returns401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid-token");

        var response = await client.GetAsync("/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetMe_WithValidToken_ReturnsUserProfile()
    {
        // Arrange - JWT contains tenant_id, user_id, role from claims
        var token = _factory.GenerateTestToken(_testUser!, _testTenant!.Id);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        profile.Should().NotBeNull();
        profile!.Email.Should().Be(_testUser.Email);
        profile.TenantId.Should().Be(_testTenant!.Id);
    }

    [Fact]
    public async Task PatchMe_UpdatesUserName()
    {
        // Arrange
        var token = _factory.GenerateTestToken(_testUser!, _testTenant!.Id);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var newName = $"Updated Name {Guid.NewGuid():N}";

        // Act
        var response = await client.PatchAsJsonAsync("/me", new { Name = newName });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var profile = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        profile!.Name.Should().Be(newName);
    }

    [Fact]
    public async Task GetTenantUsers_AsNonAdmin_Returns403()
    {
        // Arrange - Use regular user (not admin) - role comes from JWT claims
        var token = _factory.GenerateTestToken(_testUser!, _testTenant!.Id);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/tenant/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetTenantUsers_AsAdmin_ReturnsUserList()
    {
        // Arrange - Use admin user - role=admin comes from JWT claims
        var token = _factory.GenerateTestToken(_adminUser!, _testTenant!.Id);

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/tenant/users");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var users = await response.Content.ReadFromJsonAsync<List<TenantUserDto>>();
        users.Should().NotBeNull();
        users!.Should().Contain(u => u.Email == _adminUser.Email);
    }

    [Fact]
    public async Task TenantIsolation_AdminCanOnlySeeOwnTenantUsers()
    {
        // Arrange - Create a second tenant with its own user
        var (otherTenant, otherUser) = await _factory.CreateOtherTenantAsync();

        try
        {
            // Admin from testTenant tries to list users
            var token = _factory.GenerateTestToken(_adminUser!, _testTenant!.Id);

            using var client = _factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", token);

            // Act
            var response = await client.GetAsync("/tenant/users");

            // Assert - Should only see users from their own tenant
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var users = await response.Content.ReadFromJsonAsync<List<TenantUserDto>>();
            users.Should().NotBeNull();

            // Should see users from own tenant
            users!.Should().Contain(u => u.Email == _adminUser!.Email);
            users!.Should().Contain(u => u.Email == _testUser!.Email);

            // Should NOT see users from other tenant
            users!.Should().NotContain(u => u.Email == otherUser.Email);
        }
        finally
        {
            await _factory.CleanupTestDataAsync(otherTenant.Id);
        }
    }
}

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private const string TestJwtSecret = "b+q0KEu4J51XBre65LkZvZg73Afh1f1W0ZjBS7C69xONJv/2WoyJ18zTEkALkOqoE+r5FuQ91VIHMT7JQU9NAA==";
    private const string TestIssuer = "https://jcfyszulftfutsvtrghz.supabase.co/auth/v1";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    public async Task<(Tenant, User, User)> SetupTestDataAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Create a test tenant
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = $"Test Tenant {DateTime.UtcNow.Ticks}",
            Plan = "pro",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Create a regular test user
        var testUser = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = Guid.NewGuid().ToString(),
            Email = $"testuser_{Guid.NewGuid():N}@test.com",
            Name = "Test User",
            Role = UserRole.User,
            TenantId = tenant.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Create an admin test user
        var adminUser = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = Guid.NewGuid().ToString(),
            Email = $"adminuser_{Guid.NewGuid():N}@test.com",
            Name = "Admin User",
            Role = UserRole.Admin,
            TenantId = tenant.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Tenants.Add(tenant);
        dbContext.Users.Add(testUser);
        dbContext.Users.Add(adminUser);
        await dbContext.SaveChangesAsync();

        return (tenant, testUser, adminUser);
    }

    public async Task<(Tenant, User)> CreateOtherTenantAsync()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = $"Other Tenant {DateTime.UtcNow.Ticks}",
            Plan = "pro",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            SupabaseUserId = Guid.NewGuid().ToString(),
            Email = $"otheruser_{Guid.NewGuid():N}@other.com",
            Name = "Other Tenant User",
            Role = UserRole.Admin,
            TenantId = tenant.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        dbContext.Tenants.Add(tenant);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        return (tenant, user);
    }

    public async Task CleanupTestDataAsync(Guid? tenantId)
    {
        if (!tenantId.HasValue) return;

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<MnemoDbContext>();

        // Delete users first (foreign key)
        var users = await dbContext.Users.Where(u => u.TenantId == tenantId.Value).ToListAsync();
        dbContext.Users.RemoveRange(users);

        // Delete tenant
        var tenant = await dbContext.Tenants.FindAsync(tenantId.Value);
        if (tenant != null)
            dbContext.Tenants.Remove(tenant);

        await dbContext.SaveChangesAsync();
    }

    public string GenerateTestToken(User user, Guid tenantId)
    {
        // Must match how Program.cs validates: Encoding.UTF8.GetBytes(jwtSecret)
        var keyBytes = Encoding.UTF8.GetBytes(TestJwtSecret);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Include all necessary claims - this is what Supabase would include via app_metadata
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.SupabaseUserId!),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("aud", "authenticated"),
            new Claim("role", "authenticated"),
            // Custom claims from app_metadata
            new Claim("tenant_id", tenantId.ToString()),
            new Claim("user_id", user.Id.ToString()),
            new Claim("user_role", user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: TestIssuer,
            audience: "authenticated",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
