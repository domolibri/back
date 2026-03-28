namespace DomoLibri.Application.Services;

public record RegisterEditoraDto(
    string NomeEditora,
    string EmailAdmin,
    string Senha,
    string NomeAdmin);

public record RegisterEditoraResult(Guid EditoraId, string Token);

public record LoginDto(string Email, string Senha);

public record LoginResult(string Token);

public interface IAuthService
{
    Task<RegisterEditoraResult> RegisterAsync(RegisterEditoraDto dto);
    Task<LoginResult> LoginAsync(LoginDto dto);
}
