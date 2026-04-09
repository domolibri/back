namespace DomoLibri.Domain.Entities;

public class Editora
{
    protected Editora() { }

    public Editora(string nome, string slug)
    {
        if (string.IsNullOrWhiteSpace(nome)) throw new ArgumentException("Nome é obrigatório.", nameof(nome));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Slug é obrigatório.", nameof(slug));

        Id = Guid.NewGuid();
        Nome = nome.Trim();
        Slug = slug;
        DataCriacao = DateTime.UtcNow;
        Ativo = true;
    }

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public string? CorPrimaria { get; private set; }
    public DateTime DataCriacao { get; private set; }
    public bool Ativo { get; private set; }
    public ICollection<VinculoUsuarioEditora> Usuarios { get; private set; } = new List<VinculoUsuarioEditora>();

    public void AtualizarBranding(string? logoUrl, string? corPrimaria)
    {
        LogoUrl = logoUrl;
        CorPrimaria = corPrimaria;
    }

    public void Desativar() => Ativo = false;
    public void Ativar() => Ativo = true;
}