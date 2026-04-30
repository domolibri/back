using DomoLibri.Application.Services;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using DomoLibri.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace DomoLibri.Tests.Infrastructure;

public class InvitationServiceTests
{
    #region Helpers

    private record SutContext(
        DomoLibriDbContext Db,
        Mock<IEmailService> EmailMock,
        InvitationService Sut,
        Guid TenantId,
        Guid InviterId);

    private static SutContext CreateSut(Guid? tenantIdOverride = null, Guid? inviterIdOverride = null)
    {
        var tenantId = tenantIdOverride ?? Guid.NewGuid();
        var inviterId = inviterIdOverride ?? Guid.NewGuid();

        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns(tenantId);

        var userContext = new Mock<IUserContextProvider>();
        userContext.Setup(u => u.GetUserId()).Returns(inviterId);
        userContext.Setup(u => u.GetIp()).Returns("127.0.0.1");
        userContext.Setup(u => u.GetUserAgent()).Returns("Test/1.0");

        var options = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new DomoLibriDbContext(options, tenantProvider.Object);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "FrontendUrl", "http://localhost:4200" }
            })
            .Build();

        var emailMock = new Mock<IEmailService>();
        emailMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var sut = new InvitationService(db, emailMock.Object, userContext.Object, tenantProvider.Object, config);
        return new SutContext(db, emailMock, sut, tenantId, inviterId);
    }

    /// <summary>Seeds an Editora + VinculoUsuarioEditora into the DB and returns their IDs.</summary>
    private static async Task<(Editora editora, VinculoUsuarioEditora inviter, Role role)> SeedTenantAsync(
        DomoLibriDbContext db, Editora editora, Guid inviterId)
    {
        var inviterUsuario = new Usuario("admin@editora.com", "hash", "Admin Editora");

        var inviter = new VinculoUsuarioEditora
        {
            Id = inviterId,
            EditoraId = editora.Id,
            UsuarioId = inviterUsuario.Id,
            Ativo = true,
            DataEntrada = DateTime.UtcNow
        };

        var role = new Role
        {
            Id = Guid.NewGuid(),
            EditoraId = editora.Id,
            Nome = "Autor"
        };

        db.Editoras.Add(editora);
        db.Usuarios.Add(inviterUsuario);
        db.VinculosUsuarioEditora.Add(inviter);
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        return (editora, inviter, role);
    }

    #endregion

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteUser_ValidRequest_PersistsConviteAndSendsEmail()
    {
        var editora = new Editora("Editora Teste", "editora-teste");
        var ctx = CreateSut(editora.Id);
        var (_, _, role) = await SeedTenantAsync(ctx.Db, editora, ctx.InviterId);

        var result = await ctx.Sut.InviteUserAsync(new InviteUserDto("novo@editora.com", role.Id));

        // Convite persisted
        var convite = await ctx.Db.Convites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == result.ConviteId);

        Assert.NotNull(convite);
        Assert.Equal("novo@editora.com", convite.Email);
        Assert.Equal(ctx.TenantId, convite.EditoraId);
        Assert.Equal(ctx.InviterId, convite.ConvidadoPorUsuarioId);
        Assert.Equal(role.Id, convite.RoleId);
        Assert.Equal(ConviteStatus.Pendente, convite.Status);
        Assert.True(convite.DataExpiracao > DateTime.UtcNow.AddHours(47));

        // E-mail sent
        ctx.EmailMock.Verify(e =>
            e.SendEmailAsync(
                "novo@editora.com",
                It.Is<string>(s => s.Contains("Editora Teste")),
                It.Is<string>(b => b.Contains(convite.Token))),
            Times.Once);
    }

    [Fact]
    public async Task InviteUser_ValidRequest_CreatesAuditLog()
    {
        var editora = new Editora("Editora Teste", "editora-teste");
        var ctx = CreateSut(editora.Id);
        var (_, _, role) = await SeedTenantAsync(ctx.Db, editora, ctx.InviterId);

        var result = await ctx.Sut.InviteUserAsync(new InviteUserDto("auditado@editora.com", role.Id));

        var log = await ctx.Db.AuditLogs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Acao == "ConviteEnviado");

        Assert.NotNull(log);
        Assert.Equal(ctx.TenantId, log.EditoraId);
        Assert.Equal(ctx.InviterId, log.UsuarioId);
        Assert.Equal("ConviteUsuario", log.Recurso);
        Assert.Equal(result.ConviteId.ToString(), log.RecursoId);
        Assert.Contains("auditado@editora.com", log.DadosNovos);
    }

    // ── Validation guards ────────────────────────────────────────────────────

    [Fact]
    public async Task InviteUser_EmailAlreadyUser_ThrowsInvalidOperation()
    {
        var editora = new Editora("Editora Teste", "editora-teste");
        var ctx = CreateSut(editora.Id);
        var (_, _, role) = await SeedTenantAsync(ctx.Db, editora, ctx.InviterId);

        // Try to invite the inviter's own e-mail (already a user)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Sut.InviteUserAsync(new InviteUserDto("admin@editora.com", role.Id)));

        Assert.Contains("já pertence", ex.Message);
    }

    [Fact]
    public async Task InviteUser_PendingInviteExists_ThrowsInvalidOperation()
    {
        var editora = new Editora("Editora Teste", "editora-teste");
        var ctx = CreateSut(editora.Id);
        var (_, _, role) = await SeedTenantAsync(ctx.Db, editora, ctx.InviterId);

        // Seed a pre-existing pending invite for the same e-mail
        ctx.Db.Convites.Add(new ConviteUsuario
        {
            Id = Guid.NewGuid(),
            EditoraId = ctx.TenantId,
            Email = "pendente@editora.com",
            RoleId = role.Id,
            Token = "existingtoken",
            DataCriacao = DateTime.UtcNow,
            DataExpiracao = DateTime.UtcNow.AddHours(48),
            Status = ConviteStatus.Pendente,
            ConvidadoPorUsuarioId = ctx.InviterId
        });
        await ctx.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Sut.InviteUserAsync(new InviteUserDto("pendente@editora.com", role.Id)));

        Assert.Contains("convite pendente", ex.Message);
    }

    [Fact]
    public async Task InviteUser_RoleNotFound_ThrowsInvalidOperation()
    {
        var editora = new Editora("Editora Teste", "editora-teste");
        var ctx = CreateSut(editora.Id);
        await SeedTenantAsync(ctx.Db, editora, ctx.InviterId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Sut.InviteUserAsync(new InviteUserDto("novo@editora.com", Guid.NewGuid())));

        Assert.Contains("role", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── E-mail content validation (Mailpit parity) ───────────────────────────

    [Fact]
    public async Task InviteUser_EmailBody_ContainsCorrectLinkAndToken()
    {
        var editora = new Editora("Editora Teste", "editora-teste");
        var ctx = CreateSut(editora.Id);
        var (_, _, role) = await SeedTenantAsync(ctx.Db, editora, ctx.InviterId);

        string? capturedTo = null;
        string? capturedBody = null;

        ctx.EmailMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((to, _, body) =>
            {
                capturedTo = to;
                capturedBody = body;
            })
            .Returns(Task.CompletedTask);

        var result = await ctx.Sut.InviteUserAsync(new InviteUserDto("convidado@editora.com", role.Id));

        // Correct recipient
        Assert.Equal("convidado@editora.com", capturedTo);

        // Body must contain the register link with the token
        var convite = await ctx.Db.Convites
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == result.ConviteId);

        Assert.NotNull(capturedBody);
        Assert.Contains($"/cadastro/convite?token={convite.Token}", capturedBody);

        // Token is 32 hex chars (128-bit from 16 random bytes)
        Assert.Matches("^[0-9A-F]{32}$", convite.Token);

        // Body references the editora name
        Assert.Contains("Editora Teste", capturedBody);
    }

    // ── Multi-tenancy isolation ──────────────────────────────────────────────

    [Fact]
    public async Task InviteUser_TenantIsolation_TenantACannotSeeOrConflictWithTenantBInvites()
    {
        var editoraA = new Editora("Editora A", "editora-a");
        var editoraB = new Editora("Editora B", "editora-b");
        var tenantA = editoraA.Id;
        var tenantB = editoraB.Id;
        var inviterA = Guid.NewGuid();
        var inviterB = Guid.NewGuid();

        // Both contexts share the same InMemory database name
        const string sharedDbName = "isolation-test";
        var options = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(sharedDbName)
            .Options;

        var dbA = new DomoLibriDbContext(options, CreateTenantMock(tenantA));
        var dbB = new DomoLibriDbContext(options, CreateTenantMock(tenantB));

        // Seed both tenants into the shared DB
        var roleA = new Role { Id = Guid.NewGuid(), EditoraId = tenantA, Nome = "Autor" };
        var roleB = new Role { Id = Guid.NewGuid(), EditoraId = tenantB, Nome = "Editor" };

        dbA.Editoras.Add(editoraA);
        var inviterAUsuario = new Usuario("admin@a.com", "h", "Admin A");
        dbA.Usuarios.Add(inviterAUsuario);
        dbA.VinculosUsuarioEditora.Add(new VinculoUsuarioEditora { Id = inviterA, EditoraId = tenantA, UsuarioId = inviterAUsuario.Id, Ativo = true, DataEntrada = DateTime.UtcNow });
        dbA.Roles.Add(roleA);

        dbB.Editoras.Add(editoraB);
        var inviterBUsuario = new Usuario("admin@b.com", "h", "Admin B");
        dbB.Usuarios.Add(inviterBUsuario);
        dbB.VinculosUsuarioEditora.Add(new VinculoUsuarioEditora { Id = inviterB, EditoraId = tenantB, UsuarioId = inviterBUsuario.Id, Ativo = true, DataEntrada = DateTime.UtcNow });
        dbB.Roles.Add(roleB);

        await dbA.SaveChangesAsync();
        await dbB.SaveChangesAsync();

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>())).Returns(Task.CompletedTask);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "FrontendUrl", "http://localhost:4200" } })
            .Build();

        var svcA = new InvitationService(dbA, emailMock.Object, CreateUserContextMock(inviterA), CreateTenantMock(tenantA), config);
        var svcB = new InvitationService(dbB, emailMock.Object, CreateUserContextMock(inviterB), CreateTenantMock(tenantB), config);

        // Both tenants invite the SAME email — should both succeed (scoped to their own tenant)
        var resultA = await svcA.InviteUserAsync(new InviteUserDto("shared@colega.com", roleA.Id));
        var resultB = await svcB.InviteUserAsync(new InviteUserDto("shared@colega.com", roleB.Id));

        Assert.NotEqual(resultA.ConviteId, resultB.ConviteId);

        // Verify global filter: dbA only sees tenant A's invites, dbB only sees tenant B's
        var invitesA = await dbA.Convites.ToListAsync();
        var invitesB = await dbB.Convites.ToListAsync();

        Assert.All(invitesA, c => Assert.Equal(tenantA, c.EditoraId));
        Assert.All(invitesB, c => Assert.Equal(tenantB, c.EditoraId));
        Assert.DoesNotContain(invitesA, c => c.EditoraId == tenantB);
        Assert.DoesNotContain(invitesB, c => c.EditoraId == tenantA);

        // Both exist in the raw table (no filter)
        var allConvites = await dbA.Convites.IgnoreQueryFilters().ToListAsync();
        Assert.Contains(allConvites, c => c.EditoraId == tenantA);
        Assert.Contains(allConvites, c => c.EditoraId == tenantB);
    }

    // ── Private test helpers ─────────────────────────────────────────────────

    private static ITenantProvider CreateTenantMock(Guid tenantId)
    {
        var m = new Mock<ITenantProvider>();
        m.Setup(t => t.GetTenantId()).Returns(tenantId);
        return m.Object;
    }

    private static IUserContextProvider CreateUserContextMock(Guid userId)
    {
        var m = new Mock<IUserContextProvider>();
        m.Setup(u => u.GetUserId()).Returns(userId);
        m.Setup(u => u.GetIp()).Returns("127.0.0.1");
        m.Setup(u => u.GetUserAgent()).Returns("Test/1.0");
        return m.Object;
    }
}
