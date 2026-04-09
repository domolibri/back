using DomoLibri.Domain.Entities;

namespace DomoLibri.Domain.Interfaces;

public interface IUsuarioRepository
{
    Task<Usuario?> FindByEmailAsync(string email);
    Task<Usuario?> FindByIdAsync(Guid id);
    Task AddAsync(Usuario usuario);
}
