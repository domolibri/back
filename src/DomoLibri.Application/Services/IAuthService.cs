namespace DomoLibri.Application.Services;

public record RegisterEditoraDto(
    string NomeEditora,
    string EmailAdmin,
    string Senha,
    string NomeAdmin);

public record RegisterEditoraResult(Guid EditoraId);

public record LoginDto(string Email, string Senha);

public record LoginResult(string Token);

public record VerifyEmailDto(string Email, string Token);

public record ForgotPasswordDto(string Email);

public record ResetPasswordDto(string Email, string Token, string NovaSenha);

public interface IAuthService
{
    Task<RegisterEditoraResult> RegisterAsync(RegisterEditoraDto dto);
    Task<LoginResult> LoginAsync(LoginDto dto);
    Task VerifyEmailAsync(VerifyEmailDto dto);
    Task ResendVerificationEmailAsync(string email);
    Task ForgotPasswordAsync(ForgotPasswordDto dto);
    Task ResetPasswordAsync(ResetPasswordDto dto);
}
