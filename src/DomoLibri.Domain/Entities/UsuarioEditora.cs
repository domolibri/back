using DomoLibri.Domain.Enums;

namespace DomoLibri.Domain.Entities;

public class UsuarioEditora
{
    // Primary key
    public Guid Id { get; set; }

    // Foreign key for multi-tenancy isolation
    public Guid EditoraId { get; set; }

    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public string Nome { get; set; } = string.Empty;

    public Role Role { get; set; }
    public bool Ativo { get; set; }

    public bool EmailConfirmado { get; set; }
    public string? TokenConfirmacao { get; set; }
    public DateTime? ExpiracaoToken { get; set; }

    // Navigation property
    public Editora? Editora { get; set; }
}