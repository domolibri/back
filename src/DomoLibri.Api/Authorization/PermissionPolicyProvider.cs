using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace DomoLibri.Api.Authorization;

/// <summary>
/// Dynamically generates an <see cref="AuthorizationPolicy"/> for any policy name
/// that is not explicitly registered. The policy name is treated as a permission code,
/// producing a policy equivalent to <c>[Authorize(Policy = "usuarios.ler")]</c>.
/// Explicitly registered policies (e.g. role-based policies) are resolved first via
/// the base provider.
/// </summary>
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options) { }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Honour any policy that was explicitly registered (e.g. role-based policies).
        var existing = await base.GetPolicyAsync(policyName);
        if (existing is not null)
            return existing;

        // Treat every unrecognised policy name as a permission code requirement.
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName))
            .Build();
    }
}
