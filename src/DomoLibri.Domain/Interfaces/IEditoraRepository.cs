using DomoLibri.Domain.Entities;

namespace DomoLibri.Domain.Interfaces;

public interface IEditoraRepository
{
    Task<Editora?> FindByIdAsync(Guid id);
    Task<bool> SlugExisteAsync(string slug);
    Task AddAsync(Editora editora);
}
