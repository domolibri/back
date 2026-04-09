using System.Text.Json;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace DomoLibri.Tests.Infrastructure;

public class AuditInterceptorTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private (DomoLibriDbContext db, Mock<ITenantProvider> tenantMock, Mock<IUserContextProvider> userMock)
        CreateContext(Guid? tenantId = null, Guid? userId = null)
    {
        var tenantMock = new Mock<ITenantProvider>();
        tenantMock.Setup(t => t.GetTenantId()).Returns(tenantId ?? _tenantId);

        var userMock = new Mock<IUserContextProvider>();
        userMock.Setup(u => u.GetUserId()).Returns(userId ?? _userId);
        userMock.Setup(u => u.GetIp()).Returns("10.0.0.1");
        userMock.Setup(u => u.GetUserAgent()).Returns("TestAgent/1.0");

        var interceptor = new AuditInterceptor(tenantMock.Object, userMock.Object);

        var options = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        var db = new DomoLibriDbContext(options, tenantMock.Object);
        return (db, tenantMock, userMock);
    }

    // ─── Insert ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Insert_Editora_CreatesInsertAuditLog()
    {
        var (db, _, _) = CreateContext();

        var editora = new Editora("Editora Teste", "editora-teste");
        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.IgnoreQueryFilters().FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("Insert", log.Acao);
        Assert.Equal("Editora", log.Recurso);
        Assert.Equal(editora.Id.ToString(), log.RecursoId);
        Assert.Equal(editora.Id, log.EditoraId);
        Assert.Equal(_userId, log.UsuarioId);
        Assert.Equal("10.0.0.1", log.IP);
        Assert.Null(log.DadosOriginais);
        Assert.NotNull(log.DadosNovos);
    }

    [Fact]
    public async Task Insert_VinculoUsuarioEditora_CreatesInsertAuditLog()
    {
        var (db, _, _) = CreateContext();

        var editora = new Editora("E", "e");
        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        // Clear existing audit logs to isolate the VinculoUsuarioEditora insert
        db.AuditLogs.RemoveRange(db.AuditLogs.IgnoreQueryFilters());
        await db.SaveChangesAsync();

        var vinculoId = Guid.NewGuid();
        var adminUsuario = new Usuario("admin@test.com", "hash", "Admin");
        var vinculo = new VinculoUsuarioEditora
        {
            Id = vinculoId,
            EditoraId = editora.Id,
            UsuarioId = adminUsuario.Id,
            Ativo = true
        };
        db.Usuarios.Add(adminUsuario);
        db.VinculosUsuarioEditora.Add(vinculo);
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.IgnoreQueryFilters().FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal("Insert", log.Acao);
        Assert.Equal("VinculoUsuarioEditora", log.Recurso);
        Assert.Equal(vinculoId.ToString(), log.RecursoId);
    }

    // ─── Update ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_Editora_CreatesUpdateAuditLogWithBothSnapshots()
    {
        var (db, _, _) = CreateContext();

        var editora = new Editora("Original", "slug");
        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        editora.AtualizarBranding("Atualizado", null);
        await db.SaveChangesAsync();

        var logs = await db.AuditLogs.IgnoreQueryFilters().OrderBy(l => l.DataHora).ToListAsync();
        var updateLog = logs.FirstOrDefault(l => l.Acao == "Update");

        Assert.NotNull(updateLog);
        Assert.NotNull(updateLog.DadosOriginais);
        Assert.NotNull(updateLog.DadosNovos);
        Assert.Contains("Original", updateLog.DadosOriginais);
        Assert.Contains("Atualizado", updateLog.DadosNovos);
    }

    // ─── Delete ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_Editora_CreatesDeleteAuditLogWithOriginalOnly()
    {
        var (db, _, _) = CreateContext();

        var editora = new Editora("ParaExcluir", "del");
        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        db.Editoras.Remove(editora);
        await db.SaveChangesAsync();

        var deleteLog = await db.AuditLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Acao == "Delete");

        Assert.NotNull(deleteLog);
        Assert.NotNull(deleteLog.DadosOriginais);
        Assert.Null(deleteLog.DadosNovos);
        Assert.Equal("Editora", deleteLog.Recurso);
    }

    // ─── Sensitive field exclusion ────────────────────────────────────────────

    [Fact]
    public async Task Insert_Usuario_IsNotAudited_ProtectingCredentials()
    {
        // SenhaHash lives on Usuario, which is NOT in the audited entities list.
        // Inserting a Usuario must create no audit log.
        var (db, _, _) = CreateContext();

        db.Usuarios.Add(new Usuario("user@test.com", "super_secreto_hash", "Usuário"));
        await db.SaveChangesAsync();

        var count = await db.AuditLogs.IgnoreQueryFilters().CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Insert_Usuario_WithTokens_IsNotAudited_ProtectingSecurityTokens()
    {
        // Security tokens (TokenConfirmacao, TokenRedefinicaoSenha) are on Usuario,
        // which is NOT in the audited entities list. Inserting a Usuario must create no audit log.
        var (db, _, _) = CreateContext();

        var u = new Usuario("user@test.com", "h", "U");
        u.DefinirTokenConfirmacao("token_confirmacao_secreto", DateTime.UtcNow.AddHours(1));
        u.DefinirTokenRedefinicaoSenha("token_reset_secreto", DateTime.UtcNow.AddHours(1));
        db.Usuarios.Add(u);
        await db.SaveChangesAsync();

        var count = await db.AuditLogs.IgnoreQueryFilters().CountAsync();
        Assert.Equal(0, count);
    }

    // ─── Non-audited entities ─────────────────────────────────────────────────

    [Fact]
    public async Task Insert_Permission_DoesNotCreateAuditLog()
    {
        var (db, _, _) = CreateContext();

        db.Permissions.Add(new Permission
        {
            Id = Guid.NewGuid(),
            Codigo = "test.code",
            Nome = "Test",
            Agrupamento = "Test"
        });
        await db.SaveChangesAsync();

        var count = await db.AuditLogs.IgnoreQueryFilters().CountAsync();
        Assert.Equal(0, count);
    }

    // ─── Multiple entities in one SaveChanges ─────────────────────────────────

    [Fact]
    public async Task SaveChanges_WithMultipleEntities_CreatesOneLogPerEntity()
    {
        var (db, _, _) = CreateContext();

        var editora1 = new Editora("E1", "e1");
        var editora2 = new Editora("E2", "e2");
        db.Editoras.AddRange(editora1, editora2);
        await db.SaveChangesAsync();

        var count = await db.AuditLogs.IgnoreQueryFilters().CountAsync();
        Assert.Equal(2, count);
    }

    // ─── Null HTTP context (background job) ───────────────────────────────────

    [Fact]
    public async Task Insert_WithNullHttpContext_StillCreatesAuditLogWithNullIds()
    {
        var tenantMock = new Mock<ITenantProvider>();
        tenantMock.Setup(t => t.GetTenantId()).Returns((Guid?)null);

        var userMock = new Mock<IUserContextProvider>();
        userMock.Setup(u => u.GetUserId()).Returns((Guid?)null);
        userMock.Setup(u => u.GetIp()).Returns((string?)null);
        userMock.Setup(u => u.GetUserAgent()).Returns((string?)null);

        var interceptor = new AuditInterceptor(tenantMock.Object, userMock.Object);

        var options = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        var db = new DomoLibriDbContext(options, tenantMock.Object);

        db.Editoras.Add(new Editora("E", "e"));
        await db.SaveChangesAsync();

        // Use IgnoreQueryFilters since tenantProvider returns null
        var log = await db.AuditLogs.IgnoreQueryFilters().FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Null(log.UsuarioId);
        Assert.Equal(string.Empty, log.IP);
        Assert.Equal("Insert", log.Acao);
    }

    // ─── EditoraId resolution ─────────────────────────────────────────────────

    [Fact]
    public async Task Insert_VinculoUsuarioEditora_UsesEntityEditoraIdNotContextTenantId()
    {
        // Context tenant is different from the entity's EditoraId
        var (db, _, _) = CreateContext(tenantId: Guid.NewGuid());

        var editora = new Editora("E", "e");
        db.Editoras.Add(editora);
        await db.SaveChangesAsync();
        db.AuditLogs.RemoveRange(db.AuditLogs.IgnoreQueryFilters());
        await db.SaveChangesAsync();

        var usu = new Usuario("u@e.com", "h", "U");
        db.Usuarios.Add(usu);
        db.VinculosUsuarioEditora.Add(new VinculoUsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = editora.Id,
            UsuarioId = usu.Id,
            Ativo = true
        });
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.IgnoreQueryFilters().FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(editora.Id, log.EditoraId);
    }

    // ─── JSON snapshot content ────────────────────────────────────────────────

    [Fact]
    public async Task Insert_Editora_DadosNovosIsValidJson()
    {
        var (db, _, _) = CreateContext();

        db.Editoras.Add(new Editora("Json Test", "json-test"));
        await db.SaveChangesAsync();

        var log = await db.AuditLogs.IgnoreQueryFilters().FirstOrDefaultAsync();
        Assert.NotNull(log?.DadosNovos);

        // Must be parseable JSON
        var doc = JsonDocument.Parse(log.DadosNovos!);
        Assert.Equal("Json Test", doc.RootElement.GetProperty("Nome").GetString());
    }
}
