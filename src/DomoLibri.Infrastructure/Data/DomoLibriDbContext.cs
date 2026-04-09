using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
using DomoLibri.Domain.ValueObjects;
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
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<VinculoUsuarioEditora> VinculosUsuarioEditora => Set<VinculoUsuarioEditora>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ConsentimentoLGPD> ConsentimentosLGPD => Set<ConsentimentoLGPD>();
    public DbSet<ConviteUsuario> Convites => Set<ConviteUsuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Global Query Filters for Multi-Tenancy
        //    Usuario is a GLOBAL entity — no tenant filter.
        modelBuilder.Entity<VinculoUsuarioEditora>()
            .HasQueryFilter(v => v.EditoraId == _tenantProvider.GetTenantId());

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
            entity.HasIndex(e => e.Slug).IsUnique();
        });

        // Usuario: global table, Email unique across all tenants
        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.Email).IsUnique();
            entity.Property(u => u.Email)
                  .HasConversion(
                      email => email.Value,
                      value => new Email(value));
        });

        modelBuilder.Entity<VinculoUsuarioEditora>(entity =>
        {
            entity.ToTable("VinculosUsuarioEditora");
            entity.HasKey(v => v.Id);

            // Index for tenant-scoped lookups
            entity.HasIndex(v => v.EditoraId);

            // Relationship: N:1 to global Usuario
            entity.HasOne(v => v.Usuario)
                  .WithMany(u => u.Vinculos)
                  .HasForeignKey(v => v.UsuarioId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Relationship: N:1 to Editora
            entity.HasOne(v => v.Editora)
                  .WithMany(e => e.Usuarios)
                  .HasForeignKey(v => v.EditoraId)
                  .OnDelete(DeleteBehavior.Restrict);

            // N:N with Role via VinculoRoles junction table
            entity.HasMany(v => v.Roles)
                  .WithMany(r => r.Usuarios)
                  .UsingEntity(j => j.ToTable("VinculoRoles"));

            entity.Property(v => v.TipoVinculo)
                  .HasConversion<int>();
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
            entity.HasIndex(a => a.EditoraId);
            entity.HasIndex(a => a.DataHora);
            entity.HasIndex(a => new { a.EditoraId, a.DataHora });
        });

        modelBuilder.Entity<ConsentimentoLGPD>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.UsuarioId);
            entity.HasIndex(c => c.EditoraId);

            // UsuarioId references the binding (VinculoUsuarioEditora), preserving FK integrity
            // across migrations since VinculoUsuarioEditora.Id = old UsuarioEditora.Id.
            entity.HasOne<VinculoUsuarioEditora>()
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
            entity.HasIndex(c => c.Token).IsUnique();
            entity.HasIndex(c => new { c.EditoraId, c.Email });

            entity.Property(c => c.Status)
                  .HasConversion<int>();

            entity.HasOne(c => c.Editora)
                  .WithMany()
                  .HasForeignKey(c => c.EditoraId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(c => c.Role)
                  .WithMany()
                  .HasForeignKey(c => c.RoleId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(c => c.ConvidadoPor)
                  .WithMany()
                  .HasForeignKey(c => c.ConvidadoPorUsuarioId)
                  .OnDelete(DeleteBehavior.Restrict);
        });
    }
}