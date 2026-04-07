using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
using DomoLibri.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DomoLibri.Tests.Infrastructure;

public class DomoLibriDbContextTests
{
    private static DomoLibriDbContext CreateDbContext(Guid? tenantId = null)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns(tenantId);

        var options = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new DomoLibriDbContext(options, tenantProvider.Object);
    }

    [Fact]
    public async Task GlobalQueryFilter_FiltersByCurrentTenantId()
    {
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();

        // Seed data bypassing filters (use context without filter)
        using var seedDb = CreateDbContext(null);
        var editora1 = new Editora { Id = tenantId1, Nome = "E1", Slug = "e1", DataCriacao = DateTime.UtcNow, Ativo = true };
        var editora2 = new Editora { Id = tenantId2, Nome = "E2", Slug = "e2", DataCriacao = DateTime.UtcNow, Ativo = true };
        seedDb.Editoras.AddRange(editora1, editora2);
        await seedDb.SaveChangesAsync();

        // We need a shared in-memory database name to share data
        var dbName = Guid.NewGuid().ToString();
        var tenantProvider1 = new Mock<ITenantProvider>();
        tenantProvider1.Setup(t => t.GetTenantId()).Returns(tenantId1);
        var tenantProvider2 = new Mock<ITenantProvider>();
        tenantProvider2.Setup(t => t.GetTenantId()).Returns(tenantId2);

        var opts = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // Seed both vinculos in the same in-memory DB
        using var seedCtx = new DomoLibriDbContext(opts, new Mock<ITenantProvider>().Object);
        seedCtx.Editoras.AddRange(
            new Editora { Id = tenantId1, Nome = "E1", Slug = "e1-shared", DataCriacao = DateTime.UtcNow, Ativo = true },
            new Editora { Id = tenantId2, Nome = "E2", Slug = "e2-shared", DataCriacao = DateTime.UtcNow, Ativo = true }
        );
        var gu1 = new Usuario { Id = Guid.NewGuid(), Email = "u1@e.com", SenhaHash = "h", Nome = "U1" };
        var gu2 = new Usuario { Id = Guid.NewGuid(), Email = "u2@e.com", SenhaHash = "h", Nome = "U2" };
        var v1 = new VinculoUsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId1, UsuarioId = gu1.Id, Ativo = true };
        var v2 = new VinculoUsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId2, UsuarioId = gu2.Id, Ativo = true };
        seedCtx.Usuarios.AddRange(gu1, gu2);
        seedCtx.VinculosUsuarioEditora.AddRange(v1, v2);
        await seedCtx.SaveChangesAsync();

        // Query with tenant1 filter
        using var ctx1 = new DomoLibriDbContext(opts, tenantProvider1.Object);
        var vinculos1 = await ctx1.VinculosUsuarioEditora.ToListAsync();

        Assert.Single(vinculos1);
        Assert.Equal(tenantId1, vinculos1[0].EditoraId);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ReturnsAllUsers()
    {
        var tenantId1 = Guid.NewGuid();
        var tenantId2 = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();

        var opts = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns(tenantId1);

        using var seedCtx = new DomoLibriDbContext(opts, tenantProvider.Object);
        seedCtx.Editoras.AddRange(
            new Editora { Id = tenantId1, Nome = "E1", Slug = "e1", DataCriacao = DateTime.UtcNow, Ativo = true },
            new Editora { Id = tenantId2, Nome = "E2", Slug = "e2", DataCriacao = DateTime.UtcNow, Ativo = true }
        );
        var gu1 = new Usuario { Id = Guid.NewGuid(), Email = "u1@e.com", SenhaHash = "h", Nome = "U1" };
        var gu2 = new Usuario { Id = Guid.NewGuid(), Email = "u2@e.com", SenhaHash = "h", Nome = "U2" };
        seedCtx.Usuarios.AddRange(gu1, gu2);
        seedCtx.VinculosUsuarioEditora.AddRange(
            new VinculoUsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId1, UsuarioId = gu1.Id, Ativo = true },
            new VinculoUsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId2, UsuarioId = gu2.Id, Ativo = true }
        );
        await seedCtx.SaveChangesAsync();

        // With filter for tenant1, IgnoreQueryFilters should return all
        using var ctx = new DomoLibriDbContext(opts, tenantProvider.Object);
        var allVinculos = await ctx.VinculosUsuarioEditora.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, allVinculos.Count);
    }

    [Fact]
    public async Task DbSets_CanAddAndRetrieveEditoras()
    {
        using var db = CreateDbContext();
        var editora = new Editora
        {
            Id = Guid.NewGuid(),
            Nome = "Editora Teste",
            Slug = "editora-teste",
            DataCriacao = DateTime.UtcNow,
            Ativo = true
        };

        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var found = await db.Editoras.FindAsync(editora.Id);
        Assert.NotNull(found);
        Assert.Equal("Editora Teste", found.Nome);
    }

    [Fact]
    public async Task DbSets_CanAddAndRetrieveVinculosUsuarioEditora()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDbContext(tenantId);

        var editora = new Editora { Id = tenantId, Nome = "E1", Slug = "e1", DataCriacao = DateTime.UtcNow, Ativo = true };
        db.Editoras.Add(editora);

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = "admin@test.com",
            SenhaHash = "hash",
            Nome = "Admin"
        };
        var vinculo = new VinculoUsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = tenantId,
            UsuarioId = usuario.Id,
            Ativo = true
        };
        db.Usuarios.Add(usuario);
        db.VinculosUsuarioEditora.Add(vinculo);
        await db.SaveChangesAsync();

        var found = await db.VinculosUsuarioEditora.FirstOrDefaultAsync(v => v.Id == vinculo.Id);
        Assert.NotNull(found);
        Assert.Equal(tenantId, found.EditoraId);
    }

    [Fact]
    public async Task GlobalQueryFilter_WithNullTenantId_ReturnsNoUsers()
    {
        var tenantId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // Seed with a specific tenant
        var seedProvider = new Mock<ITenantProvider>();
        seedProvider.Setup(t => t.GetTenantId()).Returns(tenantId);
        using var seedCtx = new DomoLibriDbContext(opts, seedProvider.Object);
        seedCtx.Editoras.Add(new Editora { Id = tenantId, Nome = "E1", Slug = "e1", DataCriacao = DateTime.UtcNow, Ativo = true });
        var gu = new Usuario { Id = Guid.NewGuid(), Email = "u@e.com", SenhaHash = "h", Nome = "U" };
        seedCtx.Usuarios.Add(gu);
        seedCtx.VinculosUsuarioEditora.Add(new VinculoUsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId, UsuarioId = gu.Id, Ativo = true });
        await seedCtx.SaveChangesAsync();

        // Query with null tenantId — filter becomes EditoraId == null, which matches nothing
        var nullProvider = new Mock<ITenantProvider>();
        nullProvider.Setup(t => t.GetTenantId()).Returns((Guid?)null);
        using var ctx = new DomoLibriDbContext(opts, nullProvider.Object);

        var vinculos = await ctx.VinculosUsuarioEditora.ToListAsync();
        Assert.Empty(vinculos);
    }

    // ─── AuditLog ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AuditLog_CanAddAndRetrieve()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDbContext(tenantId);

        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            EditoraId = tenantId,
            UsuarioId = Guid.NewGuid(),
            Acao = "Login",
            Recurso = "VinculoUsuarioEditora",
            RecursoId = Guid.NewGuid().ToString(),
            IP = "127.0.0.1",
            UserAgent = "TestAgent/1.0",
            DataHora = DateTime.UtcNow,
            DadosOriginais = null,
            DadosNovos = null
        };

        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        var found = await db.AuditLogs.FirstOrDefaultAsync(a => a.Id == log.Id);
        Assert.NotNull(found);
        Assert.Equal("Login", found.Acao);
        Assert.Equal("127.0.0.1", found.IP);
        Assert.Equal(tenantId, found.EditoraId);
    }

    [Fact]
    public async Task AuditLog_GlobalFilter_IsolatesByTenant()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // Seed logs for both tenants
        using var seedCtx = new DomoLibriDbContext(opts, new Mock<ITenantProvider>().Object);
        seedCtx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), EditoraId = tenant1, Acao = "Login", Recurso = "VinculoUsuarioEditora", RecursoId = "r1", IP = "1.1.1.1", UserAgent = "UA", DataHora = DateTime.UtcNow },
            new AuditLog { Id = Guid.NewGuid(), EditoraId = tenant2, Acao = "Insert", Recurso = "Role", RecursoId = "r2", IP = "2.2.2.2", UserAgent = "UA", DataHora = DateTime.UtcNow }
        );
        await seedCtx.SaveChangesAsync();

        var provider1 = new Mock<ITenantProvider>();
        provider1.Setup(t => t.GetTenantId()).Returns(tenant1);
        using var ctx1 = new DomoLibriDbContext(opts, provider1.Object);

        var logs = await ctx1.AuditLogs.ToListAsync();
        Assert.Single(logs);
        Assert.Equal(tenant1, logs[0].EditoraId);
    }

    [Fact]
    public async Task AuditLog_SystemEventWithNullEditoraId_NotVisibleUnderTenantFilter()
    {
        var tenantId = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        // Seed a system-level log (no EditoraId)
        using var seedCtx = new DomoLibriDbContext(opts, new Mock<ITenantProvider>().Object);
        seedCtx.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EditoraId = null,
            Acao = "SystemEvent",
            Recurso = "System",
            RecursoId = "sys",
            IP = "0.0.0.0",
            UserAgent = "System",
            DataHora = DateTime.UtcNow
        });
        await seedCtx.SaveChangesAsync();

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns(tenantId);
        using var ctx = new DomoLibriDbContext(opts, tenantProvider.Object);

        // Tenant filter (EditoraId == tenantId) must exclude null-EditoraId system logs
        var logs = await ctx.AuditLogs.ToListAsync();
        Assert.Empty(logs);
    }

    [Fact]
    public async Task AuditLog_IgnoreQueryFilters_ReturnsAllLogs()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using var seedCtx = new DomoLibriDbContext(opts, new Mock<ITenantProvider>().Object);
        seedCtx.AuditLogs.AddRange(
            new AuditLog { Id = Guid.NewGuid(), EditoraId = tenant1, Acao = "Login", Recurso = "VinculoUsuarioEditora", RecursoId = "r1", IP = "1.1.1.1", UserAgent = "UA", DataHora = DateTime.UtcNow },
            new AuditLog { Id = Guid.NewGuid(), EditoraId = tenant2, Acao = "Insert", Recurso = "Role", RecursoId = "r2", IP = "2.2.2.2", UserAgent = "UA", DataHora = DateTime.UtcNow },
            new AuditLog { Id = Guid.NewGuid(), EditoraId = null, Acao = "SystemEvent", Recurso = "System", RecursoId = "sys", IP = "0.0.0.0", UserAgent = "System", DataHora = DateTime.UtcNow }
        );
        await seedCtx.SaveChangesAsync();

        var provider = new Mock<ITenantProvider>();
        provider.Setup(t => t.GetTenantId()).Returns(tenant1);
        using var ctx = new DomoLibriDbContext(opts, provider.Object);

        var allLogs = await ctx.AuditLogs.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(3, allLogs.Count);
    }

    // ─── ConsentimentoLGPD ───────────────────────────────────────────────────

    [Fact]
    public async Task ConsentimentoLGPD_CanAddAndRetrieve()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDbContext(tenantId);

        var editora = new Editora { Id = tenantId, Nome = "E1", Slug = "e1", DataCriacao = DateTime.UtcNow, Ativo = true };
        var usuario = new Usuario { Id = Guid.NewGuid(), Email = "u@e.com", SenhaHash = "h", Nome = "U" };
        var vinculo = new VinculoUsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId, UsuarioId = usuario.Id, Ativo = true };
        db.Editoras.Add(editora);
        db.Usuarios.Add(usuario);
        db.VinculosUsuarioEditora.Add(vinculo);

        var consentimento = new ConsentimentoLGPD
        {
            Id = Guid.NewGuid(),
            EditoraId = tenantId,
            UsuarioId = vinculo.Id,
            TipoConsentimento = "TermosDeUso",
            VersaoTermo = "1.0",
            DataConsentimento = DateTime.UtcNow
        };
        db.ConsentimentosLGPD.Add(consentimento);
        await db.SaveChangesAsync();

        var found = await db.ConsentimentosLGPD.FirstOrDefaultAsync(c => c.Id == consentimento.Id);
        Assert.NotNull(found);
        Assert.Equal("TermosDeUso", found.TipoConsentimento);
        Assert.Equal("1.0", found.VersaoTermo);
        Assert.Equal(vinculo.Id, found.UsuarioId);
        Assert.Equal(tenantId, found.EditoraId);
    }

    [Fact]
    public async Task ConsentimentoLGPD_GlobalFilter_IsolatesByTenant()
    {
        var tenant1 = Guid.NewGuid();
        var tenant2 = Guid.NewGuid();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        var dbName = Guid.NewGuid().ToString();
        var opts = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        using var seedCtx = new DomoLibriDbContext(opts, new Mock<ITenantProvider>().Object);
        seedCtx.Editoras.AddRange(
            new Editora { Id = tenant1, Nome = "E1", Slug = "e1", DataCriacao = DateTime.UtcNow, Ativo = true },
            new Editora { Id = tenant2, Nome = "E2", Slug = "e2", DataCriacao = DateTime.UtcNow, Ativo = true }
        );
        var gu1 = new Usuario { Id = Guid.NewGuid(), Email = "u1@e.com", SenhaHash = "h", Nome = "U1" };
        var gu2 = new Usuario { Id = Guid.NewGuid(), Email = "u2@e.com", SenhaHash = "h", Nome = "U2" };
        var v1 = new VinculoUsuarioEditora { Id = user1, EditoraId = tenant1, UsuarioId = gu1.Id, Ativo = true };
        var v2 = new VinculoUsuarioEditora { Id = user2, EditoraId = tenant2, UsuarioId = gu2.Id, Ativo = true };
        seedCtx.Usuarios.AddRange(gu1, gu2);
        seedCtx.VinculosUsuarioEditora.AddRange(v1, v2);
        seedCtx.ConsentimentosLGPD.AddRange(
            new ConsentimentoLGPD { Id = Guid.NewGuid(), EditoraId = tenant1, UsuarioId = user1, TipoConsentimento = "TermosDeUso", VersaoTermo = "1.0", DataConsentimento = DateTime.UtcNow },
            new ConsentimentoLGPD { Id = Guid.NewGuid(), EditoraId = tenant2, UsuarioId = user2, TipoConsentimento = "TermosDeUso", VersaoTermo = "1.0", DataConsentimento = DateTime.UtcNow }
        );
        await seedCtx.SaveChangesAsync();

        var provider1 = new Mock<ITenantProvider>();
        provider1.Setup(t => t.GetTenantId()).Returns(tenant1);
        using var ctx1 = new DomoLibriDbContext(opts, provider1.Object);

        var consents = await ctx1.ConsentimentosLGPD.ToListAsync();
        Assert.Single(consents);
        Assert.Equal(tenant1, consents[0].EditoraId);
    }
}
