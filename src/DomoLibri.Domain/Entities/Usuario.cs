using DomoLibri.Domain.Attributes;

namespace DomoLibri.Domain.Entities;

/// <summary>
/// Global identity entity. One record per unique email, shared across all Editoras.
/// Authentication credentials live here; per-tenant membership is in <see cref="VinculoUsuarioEditora"/>.
/// </summary>
public class Usuario
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    [SensitiveData]
    public string SenhaHash { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    public bool EmailConfirmado { get; set; }

    [SensitiveData]
    public string? TokenConfirmacao { get; set; }
    public DateTime? ExpiracaoToken { get; set; }

    [SensitiveData]
    public string? TokenRedefinicaoSenha { get; set; }
    public DateTime? ExpiracaoTokenRedefinicaoSenha { get; set; }
    public DateTime? SenhaAlteradaEm { get; set; }

    // Account lockout tracking
    public int AcessosFalhos { get; set; }
    public DateTime? BloqueioAte { get; set; }

    // Navigation property: all editora bindings for this user
    public ICollection<VinculoUsuarioEditora> Vinculos { get; set; } = new List<VinculoUsuarioEditora>();
}
