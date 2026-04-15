using DomoLibri.Domain.Enums;

namespace DomoLibri.Domain.Entities;

/// <summary>
/// Represents a user's membership within a specific Editora (tenant).
/// Identity and credentials live in <see cref="Usuario"/>; this entity carries only the
/// per-tenant binding data (role, status, join date).
/// </summary>
public class VinculoUsuarioEditora
{
    public Guid Id { get; set; }

    // Foreign key for multi-tenancy isolation
    public Guid EditoraId { get; set; }

    // Foreign key to the global user identity
    public Guid UsuarioId { get; set; }

    public bool Ativo { get; set; }

    public DateTime DataEntrada { get; set; } = DateTime.UtcNow;

    public TipoVinculo TipoVinculo { get; set; } = TipoVinculo.Colaborador;

    // Navigation properties
    public Usuario? Usuario { get; set; }
    public Editora? Editora { get; set; }
    public ICollection<Role> Roles { get; set; } = new List<Role>();

    public static VinculoUsuarioEditora Criar(Guid usuarioId, Guid editoraId, TipoVinculo tipoVinculo)
    {
        if (usuarioId == Guid.Empty) throw new ArgumentException("Usuário é obrigatório.", nameof(usuarioId));
        if (editoraId == Guid.Empty) throw new ArgumentException("Editora é obrigatória.", nameof(editoraId));

        return new VinculoUsuarioEditora
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            EditoraId = editoraId,
            TipoVinculo = tipoVinculo,
            Ativo = true,
            DataEntrada = DateTime.UtcNow
        };
    }

    public string ObterStatus() => Ativo ? "Ativo" : "Inativo";
}
