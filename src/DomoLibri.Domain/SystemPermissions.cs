namespace DomoLibri.Domain;

/// <summary>
/// Single source of truth for all system permissions.
/// Permissions are global (fixed in code); Roles and their assignments are per-Editora.
/// </summary>
public static class SystemPermissions
{
    // ── Code constants ─────────────────────────────────────────────────────────
    public const string UsuariosLer          = "usuarios.ler";
    public const string UsuariosEscrever     = "usuarios.escrever";
    public const string UsuariosExcluir      = "usuarios.excluir";
    public const string BrandingEditar       = "branding.editar";
    public const string ConfiguracoeGerenciar = "configuracoes.gerenciar";
    public const string ObrasLer             = "obras.ler";
    public const string ObrasEscrever        = "obras.escrever";
    public const string ObrasExcluir         = "obras.excluir";
    public const string SubmissoesCriar      = "submissoes.criar";
    public const string SubmissoesLer        = "submissoes.ler";
    public const string SubmissoesGerenciar  = "submissoes.gerenciar";
    public const string RelatoriosLer        = "relatorios.ler";

    // ── Master list (código → nome → agrupamento) ──────────────────────────────
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(UsuariosLer,           "Listar Usuários",              "Usuários"),
        new(UsuariosEscrever,      "Criar e Editar Usuários",      "Usuários"),
        new(UsuariosExcluir,       "Excluir Usuários",             "Usuários"),
        new(BrandingEditar,        "Editar Branding",              "Configurações"),
        new(ConfiguracoeGerenciar, "Gerenciar Configurações",      "Configurações"),
        new(ObrasLer,              "Listar Obras",                 "Catálogo"),
        new(ObrasEscrever,         "Criar e Editar Obras",         "Catálogo"),
        new(ObrasExcluir,          "Excluir Obras",                "Catálogo"),
        new(SubmissoesCriar,       "Submeter Conteúdo",            "Submissões"),
        new(SubmissoesLer,         "Visualizar Submissões",        "Submissões"),
        new(SubmissoesGerenciar,   "Gerenciar Submissões",         "Submissões"),
        new(RelatoriosLer,         "Visualizar Relatórios",        "Relatórios"),
    ];

    // ── Per-role permission sets ───────────────────────────────────────────────

    /// <summary>AdminEditora receives every permission.</summary>
    public static readonly string[] AdminEditoraCodes =
        All.Select(d => d.Codigo).ToArray();

    /// <summary>GestorEditorial: edição e visualização — sem acesso a configurações ou exclusões.</summary>
    public static readonly string[] GestorEditorialCodes =
    [
        UsuariosLer,
        ObrasLer, ObrasEscrever,
        SubmissoesLer, SubmissoesGerenciar,
        RelatoriosLer,
    ];

    /// <summary>Autor: limitado a submissão de conteúdo e leitura do catálogo.</summary>
    public static readonly string[] AutorCodes =
    [
        ObrasLer,
        SubmissoesCriar, SubmissoesLer,
    ];
}

/// <summary>Lightweight value object that describes a permission before it is persisted.</summary>
public record PermissionDefinition(string Codigo, string Nome, string Agrupamento);
