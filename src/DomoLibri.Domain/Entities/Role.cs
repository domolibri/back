namespace DomoLibri.Domain.Entities;

public class Role
{
    public Guid Id { get; set; }

    // Foreign key for multi-tenancy isolation
    public Guid EditoraId { get; set; }

    public string Nome { get; set; } = string.Empty;
    public string? Descricao { get; set; }

    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
    public ICollection<UsuarioEditora> Usuarios { get; set; } = new List<UsuarioEditora>();
}
