using System.Reflection;
using DomoLibri.Domain.Attributes;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DomoLibri.Infrastructure.Data;

public class AuditInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ITenantProvider _tenantProvider;
    private readonly IUserContextProvider _userContextProvider;

    public AuditInterceptor(ITenantProvider tenantProvider, IUserContextProvider userContextProvider)
    {
        _tenantProvider = tenantProvider;
        _userContextProvider = userContextProvider;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is null)
            return new ValueTask<InterceptionResult<int>>(result);

        try
        {
            // Best-effort: a bug in BuildAuditEntries must never block the main entity save.
            var auditEntries = BuildAuditEntries(eventData.Context);
            if (auditEntries.Count > 0)
                eventData.Context.Set<AuditLog>().AddRange(auditEntries);
        }
        catch
        {
            // Swallowed intentionally — audit-entry construction failures are non-fatal.
            // Once entries are added to the context they are saved in the same transaction
            // as the entity changes (atomic), so the main save integrity is preserved.
        }

        return new ValueTask<InterceptionResult<int>>(result);
    }

    private List<AuditLog> BuildAuditEntries(DbContext context)
    {
        var entries = new List<AuditLog>();

        var editoraIdFromContext = _tenantProvider.GetTenantId();
        var usuarioId = _userContextProvider.GetUserId();
        var ip = _userContextProvider.GetIp() ?? string.Empty;
        var userAgent = _userContextProvider.GetUserAgent() ?? string.Empty;
        var now = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
                continue;

            if (entry.Entity is not (Editora or VinculoUsuarioEditora))
                continue;

            // Resolve the EditoraId directly from the entity when possible.
            // For Editora, the entity itself is the tenant root, so use its Id.
            var editoraId = entry.Entity switch
            {
                VinculoUsuarioEditora v => (Guid?)v.EditoraId,
                Editora e => (Guid?)e.Id,
                _ => editoraIdFromContext
            };

            var acao = entry.State switch
            {
                EntityState.Added => "Insert",
                EntityState.Modified => "Update",
                EntityState.Deleted => "Delete",
                _ => "Unknown"
            };

            var recursoId = entry.Properties
                .FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue?.ToString()
                ?? string.Empty;

            // Snapshot before the change — only for Modified and Deleted
            string? dadosOriginais = null;
            if (entry.State is EntityState.Modified or EntityState.Deleted)
            {
                var original = entry.Properties
                    .Where(p => !IsSensitive(p.Metadata))
                    .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);
                dadosOriginais = JsonSerializer.Serialize(original, _jsonOptions);
            }

            // Snapshot after the change — only for Added and Modified
            string? dadosNovos = null;
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                var current = entry.Properties
                    .Where(p => !IsSensitive(p.Metadata))
                    .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);
                dadosNovos = JsonSerializer.Serialize(current, _jsonOptions);
            }

            entries.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                EditoraId = editoraId,
                UsuarioId = usuarioId,
                Acao = acao,
                Recurso = entry.Entity.GetType().Name,
                RecursoId = recursoId,
                IP = ip,
                UserAgent = userAgent,
                DataHora = now,
                DadosOriginais = dadosOriginais,
                DadosNovos = dadosNovos
            });
        }

        return entries;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a property must be excluded from audit snapshots.
    /// Primary signal: <see cref="SensitiveDataAttribute"/> on the CLR property.
    /// </summary>
    private static bool IsSensitive(IProperty property) =>
        property.PropertyInfo?.IsDefined(typeof(SensitiveDataAttribute), inherit: false) == true;
}
