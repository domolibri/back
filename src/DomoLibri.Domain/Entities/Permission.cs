namespace DomoLibri.Domain.Entities;

public class Permission
{
    public Guid Id { get; set; }

    // ex: "usuarios.ler"
    public string Codigo { get; set; } = string.Empty;

    public string Nome { get; set; } = string.Empty;

    // ex: "Configurações"
    public string Agrupamento { get; set; } = string.Empty;
}
