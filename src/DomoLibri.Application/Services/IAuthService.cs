namespace DomoLibri.Application.Services;

public record RegisterEditoraDto(
    string NomeEditora,
    string EmailAdmin,
    string Senha,
    string NomeAdmin,
    bool AceitouTermos);

public record RegisterEditoraResult(Guid EditoraId);

public record LoginDto(string Email, string Senha);

/// <summary>One editora the authenticated user can switch into.</summary>
public record ContextoDisponivel(
    Guid VinculoId,
    Guid EditoraId,
    string NomeEditora,
    IReadOnlyList<string> Roles);

/// <summary>
/// Result of credential validation. Contains the available editora contexts.
/// The frontend must call SelectContext to obtain a scoped JWT.
/// </summary>
public record LoginResult(
    Guid UsuarioId,
    string Nome,
    string Email,
    IReadOnlyList<ContextoDisponivel> Contextos);

/// <summary>Selects an editora context for the authenticated user to get a scoped JWT.</summary>
public record SelectContextDto(Guid UsuarioId, Guid EditoraId);

public record SelectContextResult(string Token);

public record VerifyEmailDto(string Email, string Token);

public record ForgotPasswordDto(string Email);

public record ResetPasswordDto(string Email, string Token, string NovaSenha);

public interface IAuthService
{
    Task<RegisterEditoraResult> RegisterAsync(RegisterEditoraDto dto);
    Task<LoginResult> LoginAsync(LoginDto dto);
    Task<SelectContextResult> SelectContextAsync(SelectContextDto dto);
    Task VerifyEmailAsync(VerifyEmailDto dto);
    Task ResendVerificationEmailAsync(string email);
    Task ForgotPasswordAsync(ForgotPasswordDto dto);
    Task ResetPasswordAsync(ResetPasswordDto dto);
}
