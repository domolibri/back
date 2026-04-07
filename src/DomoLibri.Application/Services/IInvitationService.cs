namespace DomoLibri.Application.Services;

public record InviteUserDto(string Email, Guid RoleId);

public record InviteUserResult(Guid ConviteId);

public interface IInvitationService
{
    /// <summary>
    /// Invites a new user to join the current tenant's editora.
    /// Validates uniqueness, persists the invite, sends an e-mail,
    /// and records an audit log entry.
    /// </summary>
    Task<InviteUserResult> InviteUserAsync(InviteUserDto dto);
}
