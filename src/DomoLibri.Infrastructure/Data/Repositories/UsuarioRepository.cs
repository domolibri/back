using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace DomoLibri.Infrastructure.Data.Repositories;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly DomoLibriDbContext _context;

    public UsuarioRepository(DomoLibriDbContext context)
    {
        _context = context;
    }

    public Task<Usuario?> FindByEmailAsync(string email)
    {
        var emailVo = new Email(email);
        return _context.Usuarios.FirstOrDefaultAsync(u => u.Email == emailVo);
    }

    public Task<Usuario?> FindByIdAsync(Guid id)
        => _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id);

    public Task AddAsync(Usuario usuario)
    {
        _context.Usuarios.Add(usuario);
        return Task.CompletedTask;
    }
}
