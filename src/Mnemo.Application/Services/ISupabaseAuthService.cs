namespace Mnemo.Application.Services;

/// <summary>
/// Service for interacting with Supabase Auth Admin API.
/// Used for creating users, sending invites, and managing auth.
/// </summary>
public interface ISupabaseAuthService
{
    /// <summary>
    /// Creates a new user in Supabase Auth.
    /// Used for new tenant signup flow.
    /// </summary>
    Task<SupabaseUserResult> CreateUserAsync(string email, string password);

    /// <summary>
    /// Invites a user via Supabase Auth.
    /// Sends an invite email with a link to set their password.
    /// </summary>
    Task<SupabaseUserResult> InviteUserAsync(string email);

    /// <summary>
    /// Deletes a user from Supabase Auth.
    /// Used for cleanup when tenant/user creation fails.
    /// </summary>
    Task<bool> DeleteUserAsync(string supabaseUserId);

    /// <summary>
    /// Sends a password reset email to the user.
    /// The user will receive an email with a link to reset their password.
    /// </summary>
    /// <param name="email">The user's email address</param>
    /// <param name="redirectTo">Optional URL to redirect to after password reset</param>
    Task<bool> SendPasswordResetAsync(string email, string? redirectTo = null);
}

public class SupabaseUserResult
{
    public bool Success { get; init; }
    public string? SupabaseUserId { get; init; }
    public string? Error { get; init; }

    public static SupabaseUserResult Succeeded(string supabaseUserId) =>
        new() { Success = true, SupabaseUserId = supabaseUserId };

    public static SupabaseUserResult Failed(string error) =>
        new() { Success = false, Error = error };
}
