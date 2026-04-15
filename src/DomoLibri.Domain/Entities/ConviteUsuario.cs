using DomoLibri.Domain.Enums;

namespace DomoLibri.Domain.Entities;

public class ConviteUsuario
{
    public Guid Id { get; set; }

    /// <summary>Multi-tenancy foreign key.</summary>
    public Guid EditoraId { get; set; }

    /// <summary>E-mail address of the person being invited.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Role to be automatically assigned when the invite is accepted.</summary>
    public Guid RoleId { get; set; }

    /// <summary>
    /// Cryptographically random token embedded in the invite link.
    /// Stored as a unique index – used to look up the invite without exposing the Id.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    public DateTime DataCriacao { get; set; } = DateTime.UtcNow;

    public DateTime DataExpiracao { get; set; }

    public DateTime? DataResposta { get; set; }

    public ConviteStatus Status { get; set; } = ConviteStatus.Pendente;

    /// <summary>The user (within the same tenant) who originated the invite.</summary>
    public Guid ConvidadoPorUsuarioId { get; set; }

    // Navigation properties
    public Editora? Editora { get; set; }
    public Role? Role { get; set; }
    public VinculoUsuarioEditora? ConvidadoPor { get; set; }
}
