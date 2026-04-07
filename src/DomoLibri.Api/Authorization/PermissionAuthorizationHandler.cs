using Microsoft.AspNetCore.Authorization;

namespace DomoLibri.Api.Authorization;

/// <summary>
/// Grants access when the authenticated user's JWT contains a <c>permission</c> claim
/// whose value matches <see cref="PermissionRequirement.Codigo"/>.
/// </summary>
public sealed class PermissionAuthorizationHandler
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.HasClaim("permission", requirement.Codigo))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
