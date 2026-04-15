namespace DomoLibri.Application.Services;

public record InviteUserDto(string Email, Guid RoleId);

public record InviteUserResult(Guid ConviteId);

public record InviteDetailsDto(string Email, string NomeEditora, Guid RoleId, bool UsuarioExiste);

public record AcceptInviteDto(string Token, string? Nome, string? Senha);

public interface IInvitationService
{
    /// <summary>
    /// Invites a new user to join the current tenant's editora.
    /// Validates uniqueness, persists the invite, sends an e-mail,
    /// and records an audit log entry.
    /// </summary>
    Task<InviteUserResult> InviteUserAsync(InviteUserDto dto);

    /// <summary>
    /// Retrieves invitation details based on the provided token.
    /// Validates existence and expiration.
    /// </summary>
    Task<InviteDetailsDto> GetInviteDetailsAsync(string token);

    /// <summary>
    /// Completes the invitation process. Creates the user identity if it doesn't exist
    /// and binds it to the editora with the pre-defined role.
    /// </summary>
    Task AcceptInviteAsync(AcceptInviteDto dto);
}
