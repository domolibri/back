namespace DomoLibri.Domain.Entities;

public class ConsentimentoLGPD
{
    public Guid Id { get; set; }

    public Guid EditoraId { get; set; }

    public Guid UsuarioId { get; set; }

    /// <summary>Type of consent granted: "TermosDeUso", "ComunicacaoMarketing", etc.</summary>
    public string TipoConsentimento { get; set; } = string.Empty;

    /// <summary>Version of the terms document accepted by the user.</summary>
    public string VersaoTermo { get; set; } = string.Empty;

    public DateTime DataConsentimento { get; set; } = DateTime.UtcNow;
}
