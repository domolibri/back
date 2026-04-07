using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
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
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ConsentimentoLGPD> ConsentimentosLGPD => Set<ConsentimentoLGPD>();
    public DbSet<ConviteUsuario> Convites => Set<ConviteUsuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Global Query Filters for Multi-Tenancy
        modelBuilder.Entity<UsuarioEditora>()
            .HasQueryFilter(u => u.EditoraId == _tenantProvider.GetTenantId());

        modelBuilder.Entity<Role>()
            .HasQueryFilter(r => r.EditoraId == _tenantProvider.GetTenantId());

        modelBuilder.Entity<AuditLog>()
            .HasQueryFilter(a => a.EditoraId == _tenantProvider.GetTenantId());

        modelBuilder.Entity<ConsentimentoLGPD>()
            .HasQueryFilter(c => c.EditoraId == _tenantProvider.GetTenantId());

        modelBuilder.Entity<ConviteUsuario>()
            .HasQueryFilter(c => c.EditoraId == _tenantProvider.GetTenantId());

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

            // N:N with Role via UsuarioRoles junction table
            entity.HasMany(u => u.Roles)
                  .WithMany(r => r.Usuarios)
                  .UsingEntity(j => j.ToTable("UsuarioRoles"));
        });

        modelBuilder.Entity<Role>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.HasOne<Editora>()
                  .WithMany()
                  .HasForeignKey(r => r.EditoraId)
                  .OnDelete(DeleteBehavior.Cascade);

            // N:N with Permission via RolePermissions junction table
            entity.HasMany(r => r.Permissions)
                  .WithMany()
                  .UsingEntity(j => j.ToTable("RolePermissions"));
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.HasIndex(p => p.Codigo).IsUnique();
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(a => a.Id);

            // Index for per-tenant queries
            entity.HasIndex(a => a.EditoraId);

            // Index for time-range queries
            entity.HasIndex(a => a.DataHora);

            // Composite index for the most common pattern: tenant logs in a time range
            entity.HasIndex(a => new { a.EditoraId, a.DataHora });
        });

        modelBuilder.Entity<ConsentimentoLGPD>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.UsuarioId);
            entity.HasIndex(c => c.EditoraId);

            entity.HasOne<UsuarioEditora>()
                  .WithMany()
                  .HasForeignKey(c => c.UsuarioId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne<Editora>()
                  .WithMany()
                  .HasForeignKey(c => c.EditoraId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConviteUsuario>(entity =>
        {
            entity.HasKey(c => c.Id);

            // Token is the invite-link key – must be globally unique
            entity.HasIndex(c => c.Token).IsUnique();

            // Prevent duplicate invites for the same email within a tenant
            entity.HasIndex(c => new { c.EditoraId, c.Email });

            // Persist the enum as an integer column
            entity.Property(c => c.Status)
                  .HasConversion<int>();

            entity.HasOne(c => c.Editora)
                  .WithMany()
                  .HasForeignKey(c => c.EditoraId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Restrict: deleting a Role should not silently remove invite records
            entity.HasOne(c => c.Role)
                  .WithMany()
                  .HasForeignKey(c => c.RoleId)
                  .OnDelete(DeleteBehavior.Restrict);

            // Restrict: preserve audit trail if the inviting user is removed
            entity.HasOne(c => c.ConvidadoPor)
                  .WithMany()
                  .HasForeignKey(c => c.ConvidadoPorUsuarioId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}