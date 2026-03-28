using DomoLibri.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DomoLibri.Infrastructure.Data;

// Interface to resolve the current tenant from the HTTP Request (e.g., JWT Claims)
public interface ITenantProvider
{
    Guid? GetTenantId();
}

public class DomoLibriDbContext : DbContext
{
    private readonly ITenantProvider _tenantProvider;

    public DomoLibriDbContext(
        DbContextOptions<DomoLibriDbContext> options,
        ITenantProvider tenantProvider) : base(options)
    {
        _tenantProvider = tenantProvider;
    }

    public DbSet<Editora> Editoras => Set<Editora>();
    public DbSet<UsuarioEditora> UsuariosEditora => Set<UsuarioEditora>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Global Query Filter for Multi-Tenancy
        // This ensures every query on UsuarioEditora automatically filters by the current EditoraId.
        // We reference _tenantProvider.GetTenantId() directly in the expression so EF Core
        // can evaluate it dynamically for each query.
        modelBuilder.Entity<UsuarioEditora>()
            .HasQueryFilter(u => u.EditoraId == _tenantProvider.GetTenantId());

        // 2. Entity Configurations
        modelBuilder.Entity<Editora>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Slug).IsUnique(); // Slug must be unique
        });

        modelBuilder.Entity<UsuarioEditora>(entity =>
        {
            entity.HasKey(u => u.Id);

            // Email must be unique per tenant (Editora)
            entity.HasIndex(u => new { u.Email, u.EditoraId }).IsUnique();

            // Relationship setup
            entity.HasOne(u => u.Editora)
                  .WithMany(e => e.Usuarios)
                  .HasForeignKey(u => u.EditoraId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}