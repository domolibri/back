namespace DomoLibri.Domain.Entities;

public class Editora
{
    // Primary key
    public Guid Id { get; set; }

    public string Nome { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public string? CorPrimaria { get; set; }

    public DateTime DataCriacao { get; set; }
    public bool Ativo { get; set; }

    // Navigation property for EF Core
    public ICollection<UsuarioEditora> Usuarios { get; set; } = new List<UsuarioEditora>();
}