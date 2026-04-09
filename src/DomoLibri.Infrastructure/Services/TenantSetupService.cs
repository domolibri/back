using System.Text.RegularExpressions;
using DomoLibri.Application.Services;
using DomoLibri.Domain;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DomoLibri.Infrastructure.Services;

public partial class TenantSetupService : ITenantSetupService
{
    private readonly DomoLibriDbContext _context;
    private readonly IEditoraRepository _editoraRepository;

    public TenantSetupService(DomoLibriDbContext context, IEditoraRepository editoraRepository)
    {
        _context = context;
        _editoraRepository = editoraRepository;
    }

    public string GerarSlug(string nome)
    {
        var slug = nome.ToLowerInvariant().Trim();
        slug = SlugRegexA().Replace(slug, "a");
        slug = SlugRegexE().Replace(slug, "e");
        slug = SlugRegexI().Replace(slug, "i");
        slug = SlugRegexO().Replace(slug, "o");
        slug = SlugRegexU().Replace(slug, "u");
        slug = SlugRegexC().Replace(slug, "c");
        slug = SlugRegexN().Replace(slug, "n");
        slug = SlugRegexNonSlug().Replace(slug, "");
        slug = SlugRegexSpaces().Replace(slug, "-");
        slug = SlugRegexDashes().Replace(slug, "-").Trim('-');
        return slug;
    }

    public async Task<bool> SlugExisteAsync(string nome)
    {
        var slug = GerarSlug(nome);
        return await _editoraRepository.SlugExisteAsync(slug);
    }

    public async Task<Role> SeedDefaultRolesAsync(Guid editoraId)
    {
        var allCodes = SystemPermissions.All.Select(d => d.Codigo).ToList();

        var existing = await _context.Permissions
            .Where(p => allCodes.Contains(p.Codigo))
            .ToDictionaryAsync(p => p.Codigo);

        var permissionMap = new Dictionary<string, Permission>(SystemPermissions.All.Count);
        foreach (var def in SystemPermissions.All)
        {
            if (!existing.TryGetValue(def.Codigo, out var perm))
            {
                perm = new Permission
                {
                    Id = Guid.NewGuid(),
                    Codigo = def.Codigo,
                    Nome = def.Nome,
                    Agrupamento = def.Agrupamento
                };
                _context.Permissions.Add(perm);
            }
            permissionMap[def.Codigo] = perm;
        }

        List<Permission> Resolve(string[] codes) =>
            codes.Select(c => permissionMap[c]).ToList();

        var adminRole = new Role
        {
            Id = Guid.NewGuid(),
            EditoraId = editoraId,
            Nome = "AdminEditora",
            Descricao = "Acesso total ao sistema.",
            Permissions = Resolve(SystemPermissions.AdminEditoraCodes)
        };

        var gestorRole = new Role
        {
            Id = Guid.NewGuid(),
            EditoraId = editoraId,
            Nome = "GestorEditorial",
            Descricao = "Permissões de edição e visualização.",
            Permissions = Resolve(SystemPermissions.GestorEditorialCodes)
        };

        var autorRole = new Role
        {
            Id = Guid.NewGuid(),
            EditoraId = editoraId,
            Nome = "Autor",
            Descricao = "Permissões limitadas a submissão de conteúdo.",
            Permissions = Resolve(SystemPermissions.AutorCodes)
        };

        _context.Roles.Add(adminRole);
        _context.Roles.Add(gestorRole);
        _context.Roles.Add(autorRole);

        return adminRole;
    }

    [GeneratedRegex(@"[àáâãäå]")] private static partial Regex SlugRegexA();
    [GeneratedRegex(@"[èéêë]")]   private static partial Regex SlugRegexE();
    [GeneratedRegex(@"[ìíîï]")]   private static partial Regex SlugRegexI();
    [GeneratedRegex(@"[òóôõö]")]  private static partial Regex SlugRegexO();
    [GeneratedRegex(@"[ùúûü]")]   private static partial Regex SlugRegexU();
    [GeneratedRegex(@"[ç]")]      private static partial Regex SlugRegexC();
    [GeneratedRegex(@"[ñ]")]      private static partial Regex SlugRegexN();
    [GeneratedRegex(@"[^a-z0-9\s\-]")] private static partial Regex SlugRegexNonSlug();
    [GeneratedRegex(@"\s+")]      private static partial Regex SlugRegexSpaces();
    [GeneratedRegex(@"\-+")]      private static partial Regex SlugRegexDashes();
}
