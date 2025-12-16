using Microsoft.AspNetCore.Authorization;
using Mnemo.Application.Services;

namespace Mnemo.Api.Authorization;

public class TenantAuthorizationHandler : AuthorizationHandler<TenantAuthorizationRequirement>
{
    private readonly ICurrentUserService _currentUser;

    public TenantAuthorizationHandler(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantAuthorizationRequirement requirement)
    {
        // User must be authenticated and have a tenant
        if (_currentUser.IsAuthenticated && _currentUser.TenantId.HasValue)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public class AdminAuthorizationHandler : AuthorizationHandler<AdminRequirement>
{
    private readonly ICurrentUserService _currentUser;

    public AdminAuthorizationHandler(ICurrentUserService currentUser)
    {
        _currentUser = currentUser;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AdminRequirement requirement)
    {
        if (_currentUser.IsAuthenticated && _currentUser.IsAdmin)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
