using DomoLibri.Api.Controllers;
using DomoLibri.Application.Services;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using DomoLibri.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace DomoLibri.Tests.Api;

/// <summary>
/// Integration tests for the complete invite flow:
/// 1. Send invite via POST /api/users/invite with new email
/// 2. Fetch invite from database to get token
/// 3. Call POST /api/users/invite/accept with token and registration data
/// 4. Verify VinculoUsuarioEditora was created correctly
/// </summary>
public class InvitationFlowTests
{
    #region Helpers

    private record FlowContext(
        UsersController Controller,
        InvitationService InvitationService,
        DomoLibriDbContext Db,
        Guid TenantId,
        Guid InviterId,
        Role Role);

    /// <summary>
    /// Sets up the full test context with database, services, and controller.
    /// Initializes an editora, inviter, and role for use in tests.
    /// </summary>
    private static async Task<FlowContext> CreateFlowContextAsync()
    {
        var tenantId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();

        // Setup mocks
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns(tenantId);

        var userContext = new Mock<IUserContextProvider>();
        userContext.Setup(u => u.GetUserId()).Returns(inviterId);
        userContext.Setup(u => u.GetIp()).Returns("127.0.0.1");
        userContext.Setup(u => u.GetUserAgent()).Returns("Test/1.0");

        // In-memory database
        var dbOptions = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new DomoLibriDbContext(dbOptions, tenantProvider.Object);

        // Configuration with frontend URL
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "FrontendUrl", "http://localhost:4200" }
            })
            .Build();

        // Email mock (capture emails sent)
        var emailMock = new Mock<IEmailService>();
        var sentEmails = new List<(string To, string Subject, string Body)>();
        emailMock
            .Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string>((to, subject, body) => sentEmails.Add((to, subject, body)))
            .Returns(Task.CompletedTask);

        // Seed database: Editora, Inviter Usuario, Inviter Vinculo, and Role
        // Note: Create a new editora and its corresponding tenant provider
        var editora = new Editora("Test Publisher", "test-publisher");
        
        // Create tenant provider with the actual editora ID for multi-tenant isolation
        var actualTenantProvider = new Mock<ITenantProvider>();
        actualTenantProvider.Setup(t => t.GetTenantId()).Returns(editora.Id);
        
        // Create a new DB context with the correct tenant ID
        var actualDbOptions = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var actualDb = new DomoLibriDbContext(actualDbOptions, actualTenantProvider.Object);
        
        var inviterUsuario = new Usuario("inviter@test.com", BCrypt.Net.BCrypt.HashPassword("Password123!"), "Test Inviter");
        var inviterVinculo = new VinculoUsuarioEditora
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
            Nome = "Collaborator"
        };

        actualDb.Editoras.Add(editora);
        actualDb.Usuarios.Add(inviterUsuario);
        actualDb.VinculosUsuarioEditora.Add(inviterVinculo);
        actualDb.Roles.Add(role);
        await actualDb.SaveChangesAsync();

        // Create InvitationService
        var invitationService = new InvitationService(actualDb, emailMock.Object, userContext.Object, actualTenantProvider.Object, config);

        // Create UsersController
        var usersController = new UsersController(invitationService, actualTenantProvider.Object, actualDb)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        return new FlowContext(usersController, invitationService, actualDb, editora.Id, inviterId, role);
    }

    #endregion

    /// <summary>
    /// Complete happy path test for the invite flow.
    /// 1. Sends an invite with a new email
    /// 2. Retrieves the invite token from database
    /// 3. Accepts the invite with registration data
    /// 4. Verifies the new user and their VinculoUsuarioEditora were created
    /// </summary>
    [Fact]
    public async Task InviteFlow_NewUser_CreatesUserAndVincloAfterAcceptance()
    {
        // ─────────────────────────────────────────────────────────────────────
        // STEP 1: Send invite via InvitationService
        // ─────────────────────────────────────────────────────────────────────
        var ctx = await CreateFlowContextAsync();
        const string newUserEmail = "newuser@example.com";

        var inviteResult = await ctx.InvitationService.InviteUserAsync(
            new InviteUserDto(newUserEmail, ctx.Role.Id));

        // Verify invite was created
        Assert.NotEqual(Guid.Empty, inviteResult.ConviteId);

        // ─────────────────────────────────────────────────────────────────────
        // STEP 2: Fetch the invite from database to get token
        // ─────────────────────────────────────────────────────────────────────
        var convite = await ctx.Db.Convites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == inviteResult.ConviteId);

        Assert.NotNull(convite);
        Assert.Equal(newUserEmail, convite.Email);
        Assert.Equal(ctx.TenantId, convite.EditoraId);
        Assert.Equal(ConviteStatus.Pendente, convite.Status);
        Assert.NotEmpty(convite.Token);

        // ─────────────────────────────────────────────────────────────────────
        // STEP 3: Call GetInviteDetailsAsync to verify usuarioExiste is false
        // ─────────────────────────────────────────────────────────────────────
        var inviteDetails = await ctx.InvitationService.GetInviteDetailsAsync(convite.Token);

        Assert.Equal(newUserEmail, inviteDetails.Email);
        Assert.Equal(ctx.Role.Id, inviteDetails.RoleId);
        Assert.False(inviteDetails.UsuarioExiste); // Should be false for new user

        // ─────────────────────────────────────────────────────────────────────
        // STEP 4: Accept the invite with full registration data
        // ─────────────────────────────────────────────────────────────────────
        const string newUserName = "New Test User";
        const string newUserPassword = "SecurePassword123!@#";

        var acceptPayload = new AcceptInviteDto(
            convite.Token,
            newUserName,
            newUserPassword);

        await ctx.InvitationService.AcceptInviteAsync(acceptPayload);

        // ─────────────────────────────────────────────────────────────────────
        // STEP 5: Verify the invite status was updated
        // ─────────────────────────────────────────────────────────────────────
        var updatedConvite = await ctx.Db.Convites
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == inviteResult.ConviteId);

        Assert.Equal(ConviteStatus.Aceito, updatedConvite.Status);
        Assert.NotNull(updatedConvite.DataResposta);
        Assert.True(updatedConvite.DataResposta <= DateTime.UtcNow);

        // ─────────────────────────────────────────────────────────────────────
        // STEP 6: Verify the new user was created in the Usuarios table
        // ─────────────────────────────────────────────────────────────────────
        var newUser = await ctx.Db.Usuarios
            .FirstOrDefaultAsync(u => u.Email == new DomoLibri.Domain.ValueObjects.Email(newUserEmail));

        Assert.NotNull(newUser);
        Assert.Equal(newUserName, newUser.Nome);
        Assert.True(BCrypt.Net.BCrypt.Verify(newUserPassword, newUser.SenhaHash));
        Assert.True(newUser.EmailConfirmado); // Should be confirmed via invite

        // ─────────────────────────────────────────────────────────────────────
        // STEP 7: Verify VinculoUsuarioEditora was created with correct role
        // ─────────────────────────────────────────────────────────────────────
        var vinculo = await ctx.Db.VinculosUsuarioEditora
            .Include(v => v.Roles)
            .FirstOrDefaultAsync(v => v.UsuarioId == newUser.Id && v.EditoraId == ctx.TenantId);

        Assert.NotNull(vinculo);
        Assert.Equal(ctx.TenantId, vinculo.EditoraId);
        Assert.Equal(newUser.Id, vinculo.UsuarioId);
        Assert.True(vinculo.Ativo);
        Assert.Contains(ctx.Role.Id, vinculo.Roles.Select(r => r.Id));

        // ─────────────────────────────────────────────────────────────────────
        // STEP 8: Verify audit log was created
        // ─────────────────────────────────────────────────────────────────────
        var auditLog = await ctx.Db.AuditLogs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Acao == "ConviteAceito" && a.RecursoId == inviteResult.ConviteId.ToString());

        Assert.NotNull(auditLog);
        Assert.Equal(ctx.TenantId, auditLog.EditoraId);
        Assert.Equal(newUser.Id, auditLog.UsuarioId);
    }

    /// <summary>
    /// Test the invite flow for an EXISTING user (usuarioExiste = true).
    /// The existing user should be able to accept the invite without providing password.
    /// </summary>
    [Fact]
    public async Task InviteFlow_ExistingUser_CreatesVincloWithoutRedefiningPassword()
    {
        // ─────────────────────────────────────────────────────────────────────
        // STEP 1: Create a test context
        // ─────────────────────────────────────────────────────────────────────
        var ctx = await CreateFlowContextAsync();
        const string existingUserEmail = "existing@example.com";

        // ─────────────────────────────────────────────────────────────────────
        // STEP 2: Pre-seed an existing user in the global identity table
        // ─────────────────────────────────────────────────────────────────────
        var existingUser = new Usuario(
            existingUserEmail,
            BCrypt.Net.BCrypt.HashPassword("OriginalPassword123!"),
            "Existing Test User");

        ctx.Db.Usuarios.Add(existingUser);
        await ctx.Db.SaveChangesAsync();

        // ─────────────────────────────────────────────────────────────────────
        // STEP 3: Invite the existing user to join the editora
        // ─────────────────────────────────────────────────────────────────────
        var inviteResult = await ctx.InvitationService.InviteUserAsync(
            new InviteUserDto(existingUserEmail, ctx.Role.Id));

        // ─────────────────────────────────────────────────────────────────────
        // STEP 4: Get the invite token
        // ─────────────────────────────────────────────────────────────────────
        var convite = await ctx.Db.Convites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == inviteResult.ConviteId);

        Assert.NotNull(convite);

        // ─────────────────────────────────────────────────────────────────────
        // STEP 5: Verify GetInviteDetailsAsync returns usuarioExiste = true
        // ─────────────────────────────────────────────────────────────────────
        var inviteDetails = await ctx.InvitationService.GetInviteDetailsAsync(convite.Token);

        Assert.True(inviteDetails.UsuarioExiste); // Should be true!
        Assert.Equal(existingUserEmail, inviteDetails.Email);

        // ─────────────────────────────────────────────────────────────────────
        // STEP 6: Accept the invite WITHOUT providing name and password
        // (The frontend would skip these fields for existing users)
        // ─────────────────────────────────────────────────────────────────────
        var acceptPayload = new AcceptInviteDto(convite.Token, null, null); // No name/password

        await ctx.InvitationService.AcceptInviteAsync(acceptPayload);

        // ─────────────────────────────────────────────────────────────────────
        // STEP 7: Verify the user object was NOT modified
        // ─────────────────────────────────────────────────────────────────────
        var userAfterAccept = await ctx.Db.Usuarios
            .FirstAsync(u => u.Email == new DomoLibri.Domain.ValueObjects.Email(existingUserEmail));

        Assert.Equal("Existing Test User", userAfterAccept.Nome);
        // Verify password hash is still the original (unchanged)
        Assert.True(BCrypt.Net.BCrypt.Verify("OriginalPassword123!", userAfterAccept.SenhaHash));

        // ─────────────────────────────────────────────────────────────────────
        // STEP 8: Verify VinculoUsuarioEditora was created correctly
        // ─────────────────────────────────────────────────────────────────────
        var vinculo = await ctx.Db.VinculosUsuarioEditora
            .Include(v => v.Roles)
            .FirstOrDefaultAsync(v => v.UsuarioId == existingUser.Id && v.EditoraId == ctx.TenantId);

        Assert.NotNull(vinculo);
        Assert.Equal(ctx.TenantId, vinculo.EditoraId);
        Assert.Equal(existingUser.Id, vinculo.UsuarioId);
        Assert.True(vinculo.Ativo);
        Assert.Contains(ctx.Role.Id, vinculo.Roles.Select(r => r.Id));

        // ─────────────────────────────────────────────────────────────────────
        // STEP 9: Verify the invite was marked as accepted
        // ─────────────────────────────────────────────────────────────────────
        var acceptedConvite = await ctx.Db.Convites
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == inviteResult.ConviteId);

        Assert.Equal(ConviteStatus.Aceito, acceptedConvite.Status);
        Assert.NotNull(acceptedConvite.DataResposta);
    }

    /// <summary>
    /// Test that accepting an invite creates an audit log entry.
    /// </summary>
    [Fact]
    public async Task InviteFlow_AcceptInvite_CreatesAuditLogForAcceptance()
    {
        var ctx = await CreateFlowContextAsync();
        const string newUserEmail = "audit@example.com";

        // Send invite
        var inviteResult = await ctx.InvitationService.InviteUserAsync(
            new InviteUserDto(newUserEmail, ctx.Role.Id));

        // Get token
        var convite = await ctx.Db.Convites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == inviteResult.ConviteId);

        // Accept invite
        await ctx.InvitationService.AcceptInviteAsync(
            new AcceptInviteDto(convite.Token, "Audit Test User", "Password123!@#"));

        // Verify audit log
        var auditLog = await ctx.Db.AuditLogs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Acao == "ConviteAceito" && a.RecursoId == inviteResult.ConviteId.ToString());

        Assert.NotNull(auditLog);
        Assert.Equal("ConviteUsuario", auditLog.Recurso);
        Assert.Equal(ctx.TenantId, auditLog.EditoraId);
    }

    /// <summary>
    /// Test that attempting to accept an expired invite fails gracefully.
    /// </summary>
    [Fact]
    public async Task InviteFlow_ExpiredInvite_ThrowsInvalidOperation()
    {
        var ctx = await CreateFlowContextAsync();

        // Manually create an expired invite
        var expiredConvite = new ConviteUsuario
        {
            Id = Guid.NewGuid(),
            EditoraId = ctx.TenantId,
            Email = "expired@example.com",
            RoleId = ctx.Role.Id,
            Token = "expiredtoken123",
            DataCriacao = DateTime.UtcNow.AddHours(-50),
            DataExpiracao = DateTime.UtcNow.AddHours(-2), // Already expired
            Status = ConviteStatus.Pendente,
            ConvidadoPorUsuarioId = ctx.InviterId
        };

        ctx.Db.Convites.Add(expiredConvite);
        await ctx.Db.SaveChangesAsync();

        // Attempt to accept the expired invite
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.InvitationService.AcceptInviteAsync(
                new AcceptInviteDto("expiredtoken123", "User", "Password123!@#")));

        Assert.Contains("expirou", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Test that accepting a previously accepted invite fails.
    /// </summary>
    [Fact]
    public async Task InviteFlow_AlreadyAcceptedInvite_ThrowsInvalidOperation()
    {
        var ctx = await CreateFlowContextAsync();
        const string userEmail = "accepted@example.com";

        // Send and accept invite once
        var inviteResult = await ctx.InvitationService.InviteUserAsync(
            new InviteUserDto(userEmail, ctx.Role.Id));

        var convite = await ctx.Db.Convites
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == inviteResult.ConviteId);

        await ctx.InvitationService.AcceptInviteAsync(
            new AcceptInviteDto(convite.Token, "User Name", "Password123!@#"));

        // Attempt to accept the same invite again
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ctx.InvitationService.AcceptInviteAsync(
                new AcceptInviteDto(convite.Token, "Another Name", "AnotherPass123!@#")));

        Assert.Contains("já foi processado", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
