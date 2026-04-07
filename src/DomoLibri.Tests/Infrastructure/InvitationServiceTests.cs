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

    /// <summary>Seeds an Editora + UsuarioEditora into the DB and returns their IDs.</summary>
    private static async Task<(Editora editora, UsuarioEditora inviter, Role role)> SeedTenantAsync(
        DomoLibriDbContext db, Guid tenantId, Guid inviterId)
    {
        var editora = new Editora
        {
            Id = tenantId,
            Nome = "Editora Teste",
            Slug = "editora-teste",
            DataCriacao = DateTime.UtcNow,
            Ativo = true
        };

        var inviter = new UsuarioEditora
        {
            Id = inviterId,
            EditoraId = tenantId,
            Email = "admin@editora.com",
            SenhaHash = "hash",
            Nome = "Admin Editora",
            Ativo = true,
            EmailConfirmado = true
        };

        var role = new Role
        {
            Id = Guid.NewGuid(),
            EditoraId = tenantId,
            Nome = "Autor"
        };

        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(inviter);
        db.Roles.Add(role);
        await db.SaveChangesAsync();

        return (editora, inviter, role);
    }

    #endregion

    // ── Happy path ───────────────────────────────────────────────────────────

    [Fact]
    public async Task InviteUser_ValidRequest_PersistsConviteAndSendsEmail()
    {
        var ctx = CreateSut();
        var (_, _, role) = await SeedTenantAsync(ctx.Db, ctx.TenantId, ctx.InviterId);

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
        var ctx = CreateSut();
        var (_, _, role) = await SeedTenantAsync(ctx.Db, ctx.TenantId, ctx.InviterId);

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
        var ctx = CreateSut();
        var (_, inviter, role) = await SeedTenantAsync(ctx.Db, ctx.TenantId, ctx.InviterId);

        // Try to invite the inviter's own e-mail (already a user)
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Sut.InviteUserAsync(new InviteUserDto(inviter.Email, role.Id)));

        Assert.Contains("já pertence", ex.Message);
    }

    [Fact]
    public async Task InviteUser_PendingInviteExists_ThrowsInvalidOperation()
    {
        var ctx = CreateSut();
        var (_, _, role) = await SeedTenantAsync(ctx.Db, ctx.TenantId, ctx.InviterId);

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
        var ctx = CreateSut();
        await SeedTenantAsync(ctx.Db, ctx.TenantId, ctx.InviterId);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.Sut.InviteUserAsync(new InviteUserDto("novo@editora.com", Guid.NewGuid())));

        Assert.Contains("role", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
