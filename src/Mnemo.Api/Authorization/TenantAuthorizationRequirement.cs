using Microsoft.AspNetCore.Authorization;

namespace Mnemo.Api.Authorization;

public class TenantAuthorizationRequirement : IAuthorizationRequirement
{
}

public class AdminRequirement : IAuthorizationRequirement
{
}
