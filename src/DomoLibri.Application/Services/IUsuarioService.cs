using System.ComponentModel.DataAnnotations;

namespace DomoLibri.Application.Services;

public record CriarUsuarioDto(
    [Required(ErrorMessage = "Nome é obrigatório.")]
    string Nome,

    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    string Email,

    [Required(ErrorMessage = "Tipo de vínculo é obrigatório.")]
    string TipoVinculo);

public record VinculoUsuarioResponseDto(
    Guid Id,
    string Nome,
    string Email,
    string TipoVinculo,
    string Status,
    DateTime DataEntrada);

public interface IUsuarioService
{
    Task<IReadOnlyList<VinculoUsuarioResponseDto>> ListarUsuariosAsync(Guid currentEditoraId);
    Task<VinculoUsuarioResponseDto> CadastrarOuVincularUsuarioAsync(CriarUsuarioDto dto, Guid currentEditoraId);
}
