namespace DomoLibri.Domain.Entities;

public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>Tenant that owns this log entry. Null for system-level events.</summary>
    public Guid? EditoraId { get; set; }

    /// <summary>User who triggered the event. Null for unauthenticated events (e.g., failed logins).</summary>
    public Guid? UsuarioId { get; set; }

    /// <summary>Action performed: "Insert", "Update", "Delete", "Login", "LoginFailed", etc.</summary>
    public string Acao { get; set; } = string.Empty;

    /// <summary>Resource type affected: "UsuarioEditora", "Role", "Editora", etc.</summary>
    public string Recurso { get; set; } = string.Empty;

    /// <summary>Identifier of the affected resource.</summary>
    public string RecursoId { get; set; } = string.Empty;

    public string IP { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;

    public DateTime DataHora { get; set; }

    /// <summary>
    /// JSON snapshot of the resource before the change.
    /// Sensitive fields must be anonymized before storage (LGPD compliance).
    /// </summary>
    public string? DadosOriginais { get; set; }

    /// <summary>
    /// JSON snapshot of the resource after the change.
    /// Sensitive fields must be anonymized before storage (LGPD compliance).
    /// </summary>
    public string? DadosNovos { get; set; }
}
