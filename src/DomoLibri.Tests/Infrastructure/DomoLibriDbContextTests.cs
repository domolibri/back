using DomoLibri.Domain.Entities;
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

        // Seed both users in the same in-memory DB
        using var seedCtx = new DomoLibriDbContext(opts, new Mock<ITenantProvider>().Object);
        seedCtx.Editoras.AddRange(
            new Editora { Id = tenantId1, Nome = "E1", Slug = "e1-shared", DataCriacao = DateTime.UtcNow, Ativo = true },
            new Editora { Id = tenantId2, Nome = "E2", Slug = "e2-shared", DataCriacao = DateTime.UtcNow, Ativo = true }
        );
        var u1 = new UsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId1, Email = "u1@e.com", SenhaHash = "h", Nome = "U1", Ativo = true };
        var u2 = new UsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId2, Email = "u2@e.com", SenhaHash = "h", Nome = "U2", Ativo = true };
        seedCtx.UsuariosEditora.AddRange(u1, u2);
        await seedCtx.SaveChangesAsync();

        // Query with tenant1 filter
        using var ctx1 = new DomoLibriDbContext(opts, tenantProvider1.Object);
        var users1 = await ctx1.UsuariosEditora.ToListAsync();

        Assert.Single(users1);
        Assert.Equal(tenantId1, users1[0].EditoraId);
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
        seedCtx.UsuariosEditora.AddRange(
            new UsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId1, Email = "u1@e.com", SenhaHash = "h", Nome = "U1", Ativo = true },
            new UsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId2, Email = "u2@e.com", SenhaHash = "h", Nome = "U2", Ativo = true }
        );
        await seedCtx.SaveChangesAsync();

        // With filter for tenant1, IgnoreQueryFilters should return all
        using var ctx = new DomoLibriDbContext(opts, tenantProvider.Object);
        var allUsers = await ctx.UsuariosEditora.IgnoreQueryFilters().ToListAsync();

        Assert.Equal(2, allUsers.Count);
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
    public async Task DbSets_CanAddAndRetrieveUsuariosEditora()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDbContext(tenantId);

        var editora = new Editora { Id = tenantId, Nome = "E1", Slug = "e1", DataCriacao = DateTime.UtcNow, Ativo = true };
        db.Editoras.Add(editora);

        var user = new UsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = tenantId,
            Email = "admin@test.com",
            SenhaHash = "hash",
            Nome = "Admin",
            Ativo = true
        };
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var found = await db.UsuariosEditora.FirstOrDefaultAsync(u => u.Id == user.Id);
        Assert.NotNull(found);
        Assert.Equal("admin@test.com", found.Email);
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
        seedCtx.UsuariosEditora.Add(new UsuarioEditora { Id = Guid.NewGuid(), EditoraId = tenantId, Email = "u@e.com", SenhaHash = "h", Nome = "U", Ativo = true });
        await seedCtx.SaveChangesAsync();

        // Query with null tenantId — filter becomes EditoraId == null, which matches nothing
        var nullProvider = new Mock<ITenantProvider>();
        nullProvider.Setup(t => t.GetTenantId()).Returns((Guid?)null);
        using var ctx = new DomoLibriDbContext(opts, nullProvider.Object);

        var users = await ctx.UsuariosEditora.ToListAsync();
        Assert.Empty(users);
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
            Recurso = "UsuarioEditora",
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
            new AuditLog { Id = Guid.NewGuid(), EditoraId = tenant1, Acao = "Login", Recurso = "UsuarioEditora", RecursoId = "r1", IP = "1.1.1.1", UserAgent = "UA", DataHora = DateTime.UtcNow },
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
            new AuditLog { Id = Guid.NewGuid(), EditoraId = tenant1, Acao = "Login", Recurso = "UsuarioEditora", RecursoId = "r1", IP = "1.1.1.1", UserAgent = "UA", DataHora = DateTime.UtcNow },
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

    [Fact]
    public async Task AuditLog_StoresDadosOriginaisAndDadosNovos()
    {
        var tenantId = Guid.NewGuid();
        using var db = CreateDbContext(tenantId);

        var dadosOriginais = """{"email":"a***@test.com","nome":"Usuário"}""";
        var dadosNovos = """{"email":"a***@test.com","nome":"Usuário Atualizado"}""";

        var log = new AuditLog
        {
            Id = Guid.NewGuid(),
            EditoraId = tenantId,
            Acao = "Update",
            Recurso = "UsuarioEditora",
            RecursoId = Guid.NewGuid().ToString(),
            IP = "192.168.0.1",
            UserAgent = "TestAgent/1.0",
            DataHora = DateTime.UtcNow,
            DadosOriginais = dadosOriginais,
            DadosNovos = dadosNovos
        };

        db.AuditLogs.Add(log);
        await db.SaveChangesAsync();

        var found = await db.AuditLogs.FirstOrDefaultAsync(a => a.Id == log.Id);
        Assert.NotNull(found);
        Assert.Equal(dadosOriginais, found.DadosOriginais);
        Assert.Equal(dadosNovos, found.DadosNovos);
    }
}

