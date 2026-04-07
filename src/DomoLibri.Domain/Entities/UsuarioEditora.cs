using DomoLibri.Domain.Attributes;

namespace DomoLibri.Domain.Entities;

public class UsuarioEditora
{
    // Primary key
    public Guid Id { get; set; }

    // Foreign key for multi-tenancy isolation
    public Guid EditoraId { get; set; }

    public string Email { get; set; } = string.Empty;

    [SensitiveData]
    public string SenhaHash { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;

    public bool Ativo { get; set; }

    public bool EmailConfirmado { get; set; }

    [SensitiveData]
    public string? TokenConfirmacao { get; set; }
    public DateTime? ExpiracaoToken { get; set; }

    [SensitiveData]
    public string? TokenRedefinicaoSenha { get; set; }
    public DateTime? ExpiracaoTokenRedefinicaoSenha { get; set; }
    public DateTime? SenhaAlteradaEm { get; set; }

    // Account Lockout tracking
    public int AcessosFalhos { get; set; }
    public DateTime? BloqueioAte { get; set; }

    // Navigation properties
    public Editora? Editora { get; set; }
    public ICollection<Role> Roles { get; set; } = new List<Role>();
}