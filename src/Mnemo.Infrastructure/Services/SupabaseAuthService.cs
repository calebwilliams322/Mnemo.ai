using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Mnemo.Application.Services;

namespace Mnemo.Infrastructure.Services;

/// <summary>
/// Implements Supabase Auth Admin operations using the service role key.
/// Uses HTTP directly for admin API calls as the .NET SDK has limited admin support.
/// </summary>
public class SupabaseAuthService : ISupabaseAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SupabaseAuthService> _logger;

    public SupabaseAuthService(
        IHttpClientFactory httpClientFactory,
        ILogger<SupabaseAuthService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Supabase");
        _logger = logger;
    }

    public async Task<SupabaseUserResult> CreateUserAsync(string email, string password)
    {
        try
        {
            // Use Admin API to create user (bypasses email confirmation)
            var response = await _httpClient.PostAsJsonAsync("/auth/v1/admin/users", new
            {
                email,
                password,
                email_confirm = true // Auto-confirm for admin-created users
            });

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Supabase user creation failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                // Try to parse error message
                try
                {
                    var errorJson = JsonDocument.Parse(errorContent);
                    var errorMsg = errorJson.RootElement.TryGetProperty("msg", out var msg)
                        ? msg.GetString()
                        : errorContent;
                    return SupabaseUserResult.Failed(errorMsg ?? "Failed to create user");
                }
                catch
                {
                    return SupabaseUserResult.Failed($"Failed to create user: {response.StatusCode}");
                }
            }

            var result = await response.Content.ReadFromJsonAsync<SupabaseAdminUserResponse>();

            if (result?.Id == null)
            {
                _logger.LogWarning("Supabase user creation returned null user for email: {Email}", email);
                return SupabaseUserResult.Failed("Failed to create user - no user ID returned");
            }

            _logger.LogInformation("Created Supabase user: {UserId} for email: {Email}", result.Id, email);
            return SupabaseUserResult.Succeeded(result.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Supabase user for email: {Email}", email);
            return SupabaseUserResult.Failed(ex.Message);
        }
    }

    public async Task<SupabaseUserResult> InviteUserAsync(string email)
    {
        try
        {
            // Use invite endpoint - sends email to user to set their password
            var response = await _httpClient.PostAsJsonAsync("/auth/v1/invite", new
            {
                email
            });

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Supabase invite failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);

                try
                {
                    var errorJson = JsonDocument.Parse(errorContent);
                    var errorMsg = errorJson.RootElement.TryGetProperty("msg", out var msg)
                        ? msg.GetString()
                        : errorContent;
                    return SupabaseUserResult.Failed(errorMsg ?? "Failed to invite user");
                }
                catch
                {
                    return SupabaseUserResult.Failed($"Failed to invite user: {response.StatusCode}");
                }
            }

            var result = await response.Content.ReadFromJsonAsync<SupabaseAdminUserResponse>();

            if (result?.Id == null)
            {
                _logger.LogWarning("Supabase invite returned null user for email: {Email}", email);
                return SupabaseUserResult.Failed("Failed to invite user - no user ID returned");
            }

            _logger.LogInformation("Invited Supabase user: {UserId} for email: {Email}", result.Id, email);
            return SupabaseUserResult.Succeeded(result.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invite Supabase user for email: {Email}", email);
            return SupabaseUserResult.Failed(ex.Message);
        }
    }

    public async Task<bool> DeleteUserAsync(string supabaseUserId)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"/auth/v1/admin/users/{supabaseUserId}");

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Supabase user deletion failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);
                return false;
            }

            _logger.LogInformation("Deleted Supabase user: {UserId}", supabaseUserId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete Supabase user: {UserId}", supabaseUserId);
            return false;
        }
    }

    public async Task<bool> SendPasswordResetAsync(string email, string? redirectTo = null)
    {
        try
        {
            // Use the recovery endpoint to send a password reset email
            object requestBody = redirectTo != null
                ? new { email, redirect_to = redirectTo }
                : new { email };

            var response = await _httpClient.PostAsJsonAsync("/auth/v1/recover", requestBody);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Supabase password reset failed: {StatusCode} - {Content}",
                    response.StatusCode, errorContent);
                return false;
            }

            _logger.LogInformation("Password reset email sent for: {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset for email: {Email}", email);
            return false;
        }
    }

    // Response model for Supabase Admin API
    private class SupabaseAdminUserResponse
    {
        public string? Id { get; set; }
        public string? Email { get; set; }
    }
}
