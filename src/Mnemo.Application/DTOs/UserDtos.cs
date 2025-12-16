namespace Mnemo.Application.DTOs;

public record UserProfileDto(
    Guid Id,
    string Email,
    string? Name,
    string Role,
    Guid TenantId,
    string TenantName,
    DateTime CreatedAt);

public record UpdateProfileRequest(string? Name);

public record TenantUserDto(
    Guid Id,
    string Email,
    string? Name,
    string Role,
    DateTime CreatedAt);

public record InviteUserRequest(
    string Email,
    string Role = "user");

public record InviteUserResponse(
    string Message,
    string InvitedEmail);
