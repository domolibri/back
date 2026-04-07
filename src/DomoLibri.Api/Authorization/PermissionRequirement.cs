using Microsoft.AspNetCore.Authorization;

namespace DomoLibri.Api.Authorization;

/// <summary>Represents a requirement that the current user holds a specific permission claim.</summary>
public sealed record PermissionRequirement(string Codigo) : IAuthorizationRequirement;
