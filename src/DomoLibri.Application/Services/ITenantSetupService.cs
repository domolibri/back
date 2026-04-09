using DomoLibri.Domain.Entities;

namespace DomoLibri.Application.Services;

public interface ITenantSetupService
{
    string GerarSlug(string nome);
    Task<bool> SlugExisteAsync(string nome);
    Task<Role> SeedDefaultRolesAsync(Guid editoraId);
}
