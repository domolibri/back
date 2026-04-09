using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DomoLibri.Infrastructure.Data.Repositories;

public class EditoraRepository : IEditoraRepository
{
    private readonly DomoLibriDbContext _context;

    public EditoraRepository(DomoLibriDbContext context)
    {
        _context = context;
    }

    public Task<Editora?> FindByIdAsync(Guid id)
        => _context.Editoras.FindAsync(id).AsTask();

    public Task<bool> SlugExisteAsync(string slug)
        => _context.Editoras.AnyAsync(e => e.Slug == slug);

    public Task AddAsync(Editora editora)
    {
        _context.Editoras.Add(editora);
        return Task.CompletedTask;
    }
}
