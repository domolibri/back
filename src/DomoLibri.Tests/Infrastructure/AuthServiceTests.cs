using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using DomoLibri.Application.Services;
using DomoLibri.Domain;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using DomoLibri.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;

namespace DomoLibri.Tests.Infrastructure;

public class AuthServiceTests
{
    #region Helpers

    private static (DomoLibriDbContext db, Mock<IEmailService> emailMock, AuthService sut) CreateSut(
        Dictionary<string, string?>? configOverrides = null,
        Mock<IUserContextProvider>? userContextMock = null)
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new DomoLibriDbContext(options, tenantProvider.Object);

        var config = new Dictionary<string, string?>
        {
            { "Jwt:Secret", "test_secret_at_least_32_characters_long!!" },
            { "Jwt:Issuer", "TestIssuer" },
            { "Jwt:Audience", "TestAudience" },
            { "Jwt:ExpiresInHours", "1" },
            { "FrontendUrl", "http://localhost:4200" }
        };

        if (configOverrides != null)
            foreach (var kv in configOverrides)
                config[kv.Key] = kv.Value;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config)
            .Build();

        var emailMock = new Mock<IEmailService>();
        emailMock.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        emailMock.Setup(e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        emailMock.Setup(e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var ctxMock = userContextMock ?? new Mock<IUserContextProvider>();
        if (userContextMock is null)
        {
            ctxMock.Setup(x => x.GetUserId()).Returns((Guid?)null);
            ctxMock.Setup(x => x.GetIp()).Returns((string?)null);
            ctxMock.Setup(x => x.GetUserAgent()).Returns((string?)null);
        }

        var sut = new AuthService(db, configuration, emailMock.Object, ctxMock.Object);
        return (db, emailMock, sut);
    }

    private static Editora CreateEditora(string nome = "Editora Teste")
    {
        var slug = nome.ToLowerInvariant().Replace(" ", "-");
        return new Editora
        {
            Id = Guid.NewGuid(),
            Nome = nome,
            Slug = slug,
            DataCriacao = DateTime.UtcNow,
            Ativo = true
        };
    }

    private static UsuarioEditora CreateUser(
        Guid editoraId,
        string senha = "Senha@123",
        bool emailConfirmado = true,
        bool ativo = true,
        string email = "admin@teste.com")
    {
        return new UsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = editoraId,
            Email = email,
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(senha),
            Nome = "Admin Teste",
            Ativo = ativo,
            EmailConfirmado = emailConfirmado
        };
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    #endregion

    #region RegisterAsync

    [Fact]
    public async Task RegisterAsync_NewEditoraAndEmail_CreatesEditoraUserAndSendsEmail()
    {
        var (db, emailMock, sut) = CreateSut();
        var dto = new RegisterEditoraDto("Editora Teste", "admin@teste.com", "Senha@123", "Admin");

        var result = await sut.RegisterAsync(dto);

        Assert.NotEqual(Guid.Empty, result.EditoraId);

        var editora = await db.Editoras.FindAsync(result.EditoraId);
        Assert.NotNull(editora);
        Assert.Equal("Editora Teste", editora.Nome);
        Assert.Equal("editora-teste", editora.Slug);
        Assert.True(editora.Ativo);

        var user = await db.UsuariosEditora.IgnoreQueryFilters().FirstOrDefaultAsync();
        Assert.NotNull(user);
        Assert.Equal("admin@teste.com", user.Email);
        Assert.True(user.Ativo);
        Assert.False(user.EmailConfirmado);
        Assert.NotNull(user.TokenConfirmacao);
        Assert.NotNull(user.ExpiracaoToken);
        Assert.True(user.ExpiracaoToken > DateTime.UtcNow);

        emailMock.Verify(
            e => e.SendVerificationEmailAsync("admin@teste.com", "Admin", It.Is<string>(s => s.Contains("verify-email"))),
            Times.Once);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateSlug_ThrowsInvalidOperationException()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora("Editora Teste");
        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var dto = new RegisterEditoraDto("Editora Teste", "outro@teste.com", "Senha@123", "Outro Admin");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => sut.RegisterAsync(dto));
        Assert.Contains("Editora Teste", ex.Message);
    }

    [Fact]
    public async Task RegisterAsync_ExistingEmail_SendsNotificationAndReturnsDummyResult()
    {
        var (db, emailMock, sut) = CreateSut();
        var editora = CreateEditora("Outra Editora");
        var user = CreateUser(editora.Id, email: "admin@teste.com");
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var dto = new RegisterEditoraDto("Nova Editora", "admin@teste.com", "Senha@123", "Admin Novo");

        var result = await sut.RegisterAsync(dto);

        Assert.Equal(editora.Id, result.EditoraId);
        emailMock.Verify(
            e => e.SendEmailAsync("admin@teste.com", "Tentativa de cadastro", It.IsAny<string>()),
            Times.Once);
        emailMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RegisterAsync_EmailIsNormalized_ToLower()
    {
        var (db, _, sut) = CreateSut();

        var dto = new RegisterEditoraDto("Editora A", "  Admin@TESTE.COM  ", "Senha@123", "Admin");
        await sut.RegisterAsync(dto);

        var user = await db.UsuariosEditora.IgnoreQueryFilters().FirstOrDefaultAsync();
        Assert.NotNull(user);
        Assert.Equal("admin@teste.com", user.Email);
    }

    #endregion

    #region RegisterAsync — Default Roles Seed

    [Fact]
    public async Task RegisterAsync_CreatesThreeDefaultRoles_ForNewEditora()
    {
        var (db, _, sut) = CreateSut();
        var dto = new RegisterEditoraDto("Editora Roles", "admin@roles.com", "Senha@123", "Admin");

        var result = await sut.RegisterAsync(dto);

        var roles = await db.Roles.IgnoreQueryFilters()
            .Where(r => r.EditoraId == result.EditoraId)
            .ToListAsync();

        Assert.Equal(3, roles.Count);
        Assert.Contains(roles, r => r.Nome == "AdminEditora");
        Assert.Contains(roles, r => r.Nome == "GestorEditorial");
        Assert.Contains(roles, r => r.Nome == "Autor");
    }

    [Fact]
    public async Task RegisterAsync_AdminUser_IsAssignedToAdminEditoraRole()
    {
        var (db, _, sut) = CreateSut();
        var dto = new RegisterEditoraDto("Editora Admin", "admin@admin.com", "Senha@123", "Admin");

        var result = await sut.RegisterAsync(dto);

        var user = await db.UsuariosEditora
            .IgnoreQueryFilters()
            .Include(u => u.Roles)
            .FirstAsync(u => u.EditoraId == result.EditoraId);

        Assert.Single(user.Roles);
        Assert.Equal("AdminEditora", user.Roles.First().Nome);
    }

    [Fact]
    public async Task RegisterAsync_SeedsAllSystemPermissions()
    {
        var (db, _, sut) = CreateSut();
        var dto = new RegisterEditoraDto("Editora Perms", "admin@perms.com", "Senha@123", "Admin");

        await sut.RegisterAsync(dto);

        var permCount = await db.Permissions.CountAsync();
        Assert.Equal(SystemPermissions.All.Count, permCount);

        foreach (var def in SystemPermissions.All)
            Assert.True(await db.Permissions.AnyAsync(p => p.Codigo == def.Codigo));
    }

    [Fact]
    public async Task RegisterAsync_AdminEditoraRole_HasAllPermissions()
    {
        var (db, _, sut) = CreateSut();
        var dto = new RegisterEditoraDto("Editora Full", "admin@full.com", "Senha@123", "Admin");

        var result = await sut.RegisterAsync(dto);

        var adminRole = await db.Roles.IgnoreQueryFilters()
            .Include(r => r.Permissions)
            .FirstAsync(r => r.EditoraId == result.EditoraId && r.Nome == "AdminEditora");

        Assert.Equal(SystemPermissions.All.Count, adminRole.Permissions.Count);
    }

    [Fact]
    public async Task RegisterAsync_AutorRole_HasLimitedPermissions()
    {
        var (db, _, sut) = CreateSut();
        var dto = new RegisterEditoraDto("Editora Autor", "admin@autor.com", "Senha@123", "Admin");

        var result = await sut.RegisterAsync(dto);

        var autorRole = await db.Roles.IgnoreQueryFilters()
            .Include(r => r.Permissions)
            .FirstAsync(r => r.EditoraId == result.EditoraId && r.Nome == "Autor");

        Assert.Equal(SystemPermissions.AutorCodes.Length, autorRole.Permissions.Count);
        Assert.All(autorRole.Permissions, p => Assert.Contains(p.Codigo, SystemPermissions.AutorCodes));
    }

    [Fact]
    public async Task RegisterAsync_SecondRegistration_DoesNotDuplicatePermissions()
    {
        var (db, _, sut) = CreateSut();

        await sut.RegisterAsync(new RegisterEditoraDto("Editora 1", "a@e1.com", "Senha@123", "Admin1"));
        await sut.RegisterAsync(new RegisterEditoraDto("Editora 2", "b@e2.com", "Senha@123", "Admin2"));

        // Permissions are global — count must remain the same after two tenants are registered.
        var permCount = await db.Permissions.CountAsync();
        Assert.Equal(SystemPermissions.All.Count, permCount);
    }

    #endregion

    #region RegisterAsync — Slug Generation (GerarSlug via RegisterAsync)

    [Theory]
    [InlineData("São Paulo Editora", "sao-paulo-editora")]
    [InlineData("Café Açaí Ltda", "cafe-acai-ltda")]
    [InlineData("Editora Ñoño", "editora-nono")]
    [InlineData("Hello  World", "hello-world")]
    [InlineData("---Test---", "test")]
    [InlineData("Special@#Chars!", "specialchars")]
    [InlineData("Editora com Çedilha", "editora-com-cedilha")]
    public async Task RegisterAsync_SlugGeneration_HandlesVariousInputs(string nomeEditora, string expectedSlug)
    {
        var (db, _, sut) = CreateSut();
        var email = $"{Guid.NewGuid():N}@teste.com";
        var dto = new RegisterEditoraDto(nomeEditora, email, "Senha@123", "Admin");

        var result = await sut.RegisterAsync(dto);

        var editora = await db.Editoras.FindAsync(result.EditoraId);
        Assert.NotNull(editora);
        Assert.Equal(expectedSlug, editora.Slug);
    }

    #endregion

    #region LoginAsync

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsJwtToken()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, "Senha@123");
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var result = await sut.LoginAsync(new LoginDto("admin@teste.com", "Senha@123"));

        Assert.NotNull(result.Token);
        Assert.NotEmpty(result.Token);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(result.Token);
        Assert.Equal("admin@teste.com", jwt.Claims.First(c => c.Type == "email").Value);
        Assert.Equal(editora.Id.ToString(), jwt.Claims.First(c => c.Type == "tenant_id").Value);
        Assert.Equal(user.Id.ToString(), jwt.Claims.First(c => c.Type == JwtRegisteredClaimNames.Sub).Value);
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_ThrowsUnauthorizedAccessException()
    {
        var (_, _, sut) = CreateSut();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto("notexist@test.com", "Senha@123")));
        Assert.Equal("Credenciais inválidas.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_InactiveUser_ThrowsUnauthorizedAccessException()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, ativo: false);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto(user.Email, "Senha@123")));
        Assert.Equal("Usuário inativo.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_UnverifiedEmail_ThrowsUnauthorizedAccessException()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, emailConfirmado: false);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto(user.Email, "Senha@123")));
        Assert.Equal("E-mail não verificado.", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_AccountLocked_ThrowsWithRemainingTimeMessage()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id);
        user.BloqueioAte = DateTime.UtcNow.AddMinutes(10);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto(user.Email, "Senha@123")));
        Assert.Contains("bloqueada", ex.Message);
        Assert.Contains("minuto", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_IncrementsFailedAttempts()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto(user.Email, "WrongPassword")));

        var updated = await db.UsuariosEditora.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(1, updated.AcessosFalhos);
        Assert.Null(updated.BloqueioAte);
    }

    [Fact]
    public async Task LoginAsync_FifthWrongPassword_LocksAccountAndResetsCounter()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id);
        user.AcessosFalhos = 4;
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto(user.Email, "WrongPassword")));

        var updated = await db.UsuariosEditora.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(0, updated.AcessosFalhos);
        Assert.NotNull(updated.BloqueioAte);
        Assert.True(updated.BloqueioAte > DateTime.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task LoginAsync_SuccessAfterPreviousFailures_ResetsLockoutState()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, "Senha@123");
        user.AcessosFalhos = 3;
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var result = await sut.LoginAsync(new LoginDto(user.Email, "Senha@123"));

        Assert.NotNull(result.Token);
        var updated = await db.UsuariosEditora.IgnoreQueryFilters().FirstAsync();
        Assert.Equal(0, updated.AcessosFalhos);
        Assert.Null(updated.BloqueioAte);
    }

    [Fact]
    public async Task LoginAsync_ShortJwtSecret_ThrowsInvalidOperationException()
    {
        var (db, _, sut) = CreateSut(new Dictionary<string, string?> { { "Jwt:Secret", "tooshort" } });
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, "Senha@123");
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.LoginAsync(new LoginDto(user.Email, "Senha@123")));
        Assert.Contains("32", ex.Message);
    }

    [Fact]
    public async Task LoginAsync_EmailIsTrimmedAndLowercased()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, "Senha@123", email: "admin@teste.com");
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        // Login with differently-cased email with spaces
        var result = await sut.LoginAsync(new LoginDto("  ADMIN@TESTE.COM  ", "Senha@123"));
        Assert.NotNull(result.Token);
    }

    #endregion

    #region LoginAsync — JWT Claims

    [Fact]
    public async Task LoginAsync_Jwt_ContainsRoleAndPermissionClaims_WhenUserHasRoles()
    {
        var (db, _, sut) = CreateSut();

        // RegisterAsync seeds the full role+permission graph for the editora.
        var registerDto = new RegisterEditoraDto("Editora JWT", "admin@jwt.com", "Senha@123", "Admin");
        var registerResult = await sut.RegisterAsync(registerDto);

        // Confirm the email so login is allowed.
        var user = await db.UsuariosEditora.IgnoreQueryFilters()
            .FirstAsync(u => u.EditoraId == registerResult.EditoraId);
        user.EmailConfirmado = true;
        await db.SaveChangesAsync();

        var loginResult = await sut.LoginAsync(new LoginDto("admin@jwt.com", "Senha@123"));

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(loginResult.Token);

        // Role claim — AdminEditora was assigned during registration
        var roleClaims = jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToList();
        Assert.Contains("AdminEditora", roleClaims);

        // Permission claims — AdminEditora carries all system permissions
        var permClaims = jwt.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToList();
        Assert.Equal(SystemPermissions.All.Count, permClaims.Count);
        foreach (var def in SystemPermissions.All)
            Assert.Contains(def.Codigo, permClaims);
    }

    [Fact]
    public async Task LoginAsync_Jwt_PermissionsAreDeduped_WhenRolesOverlap()
    {
        var (db, _, sut) = CreateSut();

        var registerDto = new RegisterEditoraDto("Editora Dedup", "admin@dedup.com", "Senha@123", "Admin");
        var registerResult = await sut.RegisterAsync(registerDto);

        // Give the admin user a second role (GestorEditorial) that shares permissions.
        var adminUser = await db.UsuariosEditora.IgnoreQueryFilters()
            .Include(u => u.Roles)
            .FirstAsync(u => u.EditoraId == registerResult.EditoraId);

        var gestorRole = await db.Roles.IgnoreQueryFilters()
            .FirstAsync(r => r.EditoraId == registerResult.EditoraId && r.Nome == "GestorEditorial");

        adminUser.Roles.Add(gestorRole);
        adminUser.EmailConfirmado = true;
        await db.SaveChangesAsync();

        var loginResult = await sut.LoginAsync(new LoginDto("admin@dedup.com", "Senha@123"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginResult.Token);
        var permClaims = jwt.Claims.Where(c => c.Type == "permission").Select(c => c.Value).ToList();

        // No duplicates even though both roles share permissions
        Assert.Equal(permClaims.Distinct().Count(), permClaims.Count);
        // Still covers all permissions from AdminEditora (the superset)
        Assert.Equal(SystemPermissions.All.Count, permClaims.Count);
    }

    [Fact]
    public async Task LoginAsync_Jwt_ContainsNoPermissionClaims_WhenUserHasNoRoles()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, "Senha@123");
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var result = await sut.LoginAsync(new LoginDto(user.Email, "Senha@123"));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "permission");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "role");
    }

    #endregion

    #region VerifyEmailAsync

    [Fact]
    public async Task VerifyEmailAsync_ValidToken_ConfirmsEmailAndClearsToken()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        const string plainToken = "my_test_token_12345";
        var user = CreateUser(editora.Id, emailConfirmado: false);
        user.TokenConfirmacao = HashToken(plainToken);
        user.ExpiracaoToken = DateTime.UtcNow.AddHours(24);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await sut.VerifyEmailAsync(new VerifyEmailDto(user.Email, plainToken));

        var updated = await db.UsuariosEditora.IgnoreQueryFilters().FirstAsync();
        Assert.True(updated.EmailConfirmado);
        Assert.Null(updated.TokenConfirmacao);
        Assert.Null(updated.ExpiracaoToken);
    }

    [Fact]
    public async Task VerifyEmailAsync_UserNotFound_ThrowsInvalidOperationException()
    {
        var (_, _, sut) = CreateSut();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.VerifyEmailAsync(new VerifyEmailDto("notexist@test.com", "sometoken")));
        Assert.Contains("inválido", ex.Message);
    }

    [Fact]
    public async Task VerifyEmailAsync_WrongToken_ThrowsInvalidOperationException()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, emailConfirmado: false);
        user.TokenConfirmacao = HashToken("correct_token");
        user.ExpiracaoToken = DateTime.UtcNow.AddHours(24);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.VerifyEmailAsync(new VerifyEmailDto(user.Email, "wrong_token")));
        Assert.Contains("inválido", ex.Message);
    }

    [Fact]
    public async Task VerifyEmailAsync_ExpiredToken_ThrowsInvalidOperationException()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        const string plainToken = "expired_token_xyz";
        var user = CreateUser(editora.Id, emailConfirmado: false);
        user.TokenConfirmacao = HashToken(plainToken);
        user.ExpiracaoToken = DateTime.UtcNow.AddHours(-1); // expired
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.VerifyEmailAsync(new VerifyEmailDto(user.Email, plainToken)));
        Assert.Contains("expirado", ex.Message);
    }

    [Fact]
    public async Task VerifyEmailAsync_AlreadyConfirmed_ThrowsInvalidOperationException()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        const string plainToken = "valid_token_abc";
        var user = CreateUser(editora.Id, emailConfirmado: true);
        user.TokenConfirmacao = HashToken(plainToken);
        user.ExpiracaoToken = DateTime.UtcNow.AddHours(24);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.VerifyEmailAsync(new VerifyEmailDto(user.Email, plainToken)));
        Assert.Contains("confirmado", ex.Message);
    }

    #endregion

    #region ResendVerificationEmailAsync

    [Fact]
    public async Task ResendVerificationEmailAsync_UnverifiedUser_UpdatesTokenAndSendsEmail()
    {
        var (db, emailMock, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, emailConfirmado: false);
        user.TokenConfirmacao = HashToken("old_token");
        user.ExpiracaoToken = DateTime.UtcNow.AddHours(1);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var oldHash = user.TokenConfirmacao;
        await sut.ResendVerificationEmailAsync(user.Email);

        var updated = await db.UsuariosEditora.IgnoreQueryFilters().FirstAsync();
        Assert.NotEqual(oldHash, updated.TokenConfirmacao);
        Assert.True(updated.ExpiracaoToken > DateTime.UtcNow.AddHours(23));

        emailMock.Verify(
            e => e.SendVerificationEmailAsync(user.Email, user.Nome, It.Is<string>(s => s.Contains("verify-email"))),
            Times.Once);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_UserNotFound_ReturnsSilently()
    {
        var (_, emailMock, sut) = CreateSut();

        // Should not throw
        await sut.ResendVerificationEmailAsync("notexist@test.com");

        emailMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ResendVerificationEmailAsync_AlreadyConfirmed_ReturnsSilently()
    {
        var (db, emailMock, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, emailConfirmado: true);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await sut.ResendVerificationEmailAsync(user.Email);

        emailMock.Verify(
            e => e.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    #endregion

    #region ForgotPasswordAsync

    [Fact]
    public async Task ForgotPasswordAsync_ValidUser_SetsResetTokenAndSendsEmail()
    {
        var (db, emailMock, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await sut.ForgotPasswordAsync(new ForgotPasswordDto(user.Email));

        var updated = await db.UsuariosEditora.IgnoreQueryFilters().FirstAsync();
        Assert.NotNull(updated.TokenRedefinicaoSenha);
        Assert.NotNull(updated.ExpiracaoTokenRedefinicaoSenha);
        Assert.True(updated.ExpiracaoTokenRedefinicaoSenha > DateTime.UtcNow.AddMinutes(59));

        emailMock.Verify(
            e => e.SendPasswordResetEmailAsync(user.Email, user.Nome, It.Is<string>(s => s.Contains("redefinir-senha"))),
            Times.Once);
    }

    [Fact]
    public async Task ForgotPasswordAsync_UserNotFound_ReturnsSilently()
    {
        var (_, emailMock, sut) = CreateSut();

        await sut.ForgotPasswordAsync(new ForgotPasswordDto("notexist@test.com"));

        emailMock.Verify(
            e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordAsync_InactiveUser_ReturnsSilently()
    {
        var (db, emailMock, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, ativo: false);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await sut.ForgotPasswordAsync(new ForgotPasswordDto(user.Email));

        emailMock.Verify(
            e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ForgotPasswordAsync_UnverifiedEmail_ReturnsSilently()
    {
        var (db, emailMock, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, emailConfirmado: false);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await sut.ForgotPasswordAsync(new ForgotPasswordDto(user.Email));

        emailMock.Verify(
            e => e.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    #endregion

    #region ResetPasswordAsync

    [Fact]
    public async Task ResetPasswordAsync_ValidToken_UpdatesPasswordAndClearsToken()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        const string resetToken = "valid_reset_token_xyz";
        var user = CreateUser(editora.Id);
        user.TokenRedefinicaoSenha = HashToken(resetToken);
        user.ExpiracaoTokenRedefinicaoSenha = DateTime.UtcNow.AddHours(1);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await sut.ResetPasswordAsync(new ResetPasswordDto(user.Email, resetToken, "NovaSenha@456"));

        var updated = await db.UsuariosEditora.IgnoreQueryFilters().FirstAsync();
        Assert.Null(updated.TokenRedefinicaoSenha);
        Assert.Null(updated.ExpiracaoTokenRedefinicaoSenha);
        Assert.True(BCrypt.Net.BCrypt.Verify("NovaSenha@456", updated.SenhaHash));
    }

    [Fact]
    public async Task ResetPasswordAsync_UserNotFound_ThrowsInvalidOperationException()
    {
        var (_, _, sut) = CreateSut();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ResetPasswordAsync(new ResetPasswordDto("notexist@test.com", "token", "NovaSenha@456")));
        Assert.Contains("inválido", ex.Message);
    }

    [Fact]
    public async Task ResetPasswordAsync_WrongToken_ThrowsInvalidOperationException()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        var user = CreateUser(editora.Id);
        user.TokenRedefinicaoSenha = HashToken("correct_reset_token");
        user.ExpiracaoTokenRedefinicaoSenha = DateTime.UtcNow.AddHours(1);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ResetPasswordAsync(new ResetPasswordDto(user.Email, "wrong_token", "NovaSenha@456")));
        Assert.Contains("inválido", ex.Message);
    }

    [Fact]
    public async Task ResetPasswordAsync_ExpiredToken_ThrowsInvalidOperationException()
    {
        var (db, _, sut) = CreateSut();
        var editora = CreateEditora();
        const string resetToken = "expired_reset_token";
        var user = CreateUser(editora.Id);
        user.TokenRedefinicaoSenha = HashToken(resetToken);
        user.ExpiracaoTokenRedefinicaoSenha = DateTime.UtcNow.AddHours(-1); // expired
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ResetPasswordAsync(new ResetPasswordDto(user.Email, resetToken, "NovaSenha@456")));
        Assert.Contains("expirado", ex.Message);
    }

    #endregion

    #region Audit Logs — LoginAsync

    private static Mock<IUserContextProvider> BuildContextMock(
        string ip = "192.168.0.1", string userAgent = "TestBrowser/1.0")
    {
        var mock = new Mock<IUserContextProvider>();
        mock.Setup(x => x.GetUserId()).Returns((Guid?)null);
        mock.Setup(x => x.GetIp()).Returns(ip);
        mock.Setup(x => x.GetUserAgent()).Returns(userAgent);
        return mock;
    }

    [Fact]
    public async Task LoginAsync_Success_LogsLoginSucesso()
    {
        var ctxMock = BuildContextMock("10.0.0.1", "Chrome/120");
        var (db, _, sut) = CreateSut(userContextMock: ctxMock);
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, "Senha@123");
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await sut.LoginAsync(new LoginDto(user.Email, "Senha@123"));

        var log = await db.AuditLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Acao == "LoginSucesso");

        Assert.NotNull(log);
        Assert.Equal("Auth", log.Recurso);
        Assert.Equal(user.Email, log.RecursoId);
        Assert.Equal(user.EditoraId, log.EditoraId);
        Assert.Equal(user.Id, log.UsuarioId);
        Assert.Equal("10.0.0.1", log.IP);
        Assert.Equal("Chrome/120", log.UserAgent);
    }

    [Fact]
    public async Task LoginAsync_WrongPassword_LogsLoginFalha()
    {
        var ctxMock = BuildContextMock("10.0.0.2", "Firefox/120");
        var (db, _, sut) = CreateSut(userContextMock: ctxMock);
        var editora = CreateEditora();
        var user = CreateUser(editora.Id, "Senha@123");
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto(user.Email, "WrongPassword")));

        var log = await db.AuditLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Acao == "LoginFalha");

        Assert.NotNull(log);
        Assert.Equal("Auth", log.Recurso);
        Assert.Equal(user.Email, log.RecursoId);
        Assert.Equal(user.EditoraId, log.EditoraId);
        Assert.Equal("10.0.0.2", log.IP);
    }

    [Fact]
    public async Task LoginAsync_AccountLocked_LogsLoginFalha()
    {
        var ctxMock = BuildContextMock("10.0.0.3");
        var (db, _, sut) = CreateSut(userContextMock: ctxMock);
        var editora = CreateEditora();
        var user = CreateUser(editora.Id);
        user.BloqueioAte = DateTime.UtcNow.AddMinutes(10);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto(user.Email, "Senha@123")));

        var log = await db.AuditLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Acao == "LoginFalha");

        Assert.NotNull(log);
        Assert.Equal(user.EditoraId, log.EditoraId);
        Assert.Equal(user.Id, log.UsuarioId);
    }

    [Fact]
    public async Task LoginAsync_UserNotFound_LogsLoginFalha_WithNullEditoraId()
    {
        var ctxMock = BuildContextMock("10.0.0.4");
        var (db, _, sut) = CreateSut(userContextMock: ctxMock);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => sut.LoginAsync(new LoginDto("ghost@test.com", "x")));

        var log = await db.AuditLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Acao == "LoginFalha");

        Assert.NotNull(log);
        Assert.Null(log.EditoraId);
        Assert.Null(log.UsuarioId);
        Assert.Equal("ghost@test.com", log.RecursoId);
    }

    #endregion

    #region Audit Logs — ResetPasswordAsync

    [Fact]
    public async Task ResetPasswordAsync_Success_LogsRedefinicaoSenha()
    {
        var ctxMock = BuildContextMock("10.0.0.5", "Mobile/Safari");
        var (db, _, sut) = CreateSut(userContextMock: ctxMock);
        var editora = CreateEditora();
        const string resetToken = "audit_reset_token_xyz";
        var user = CreateUser(editora.Id);
        user.TokenRedefinicaoSenha = HashToken(resetToken);
        user.ExpiracaoTokenRedefinicaoSenha = DateTime.UtcNow.AddHours(1);
        db.Editoras.Add(editora);
        db.UsuariosEditora.Add(user);
        await db.SaveChangesAsync();

        await sut.ResetPasswordAsync(new ResetPasswordDto(user.Email, resetToken, "NovaSenha@456"));

        var log = await db.AuditLogs.IgnoreQueryFilters()
            .FirstOrDefaultAsync(l => l.Acao == "RedefinicaoSenha");

        Assert.NotNull(log);
        Assert.Equal("Auth", log.Recurso);
        Assert.Equal(user.Email, log.RecursoId);
        Assert.Equal(user.EditoraId, log.EditoraId);
        Assert.Equal(user.Id, log.UsuarioId);
        Assert.Equal("10.0.0.5", log.IP);
        Assert.Equal("Mobile/Safari", log.UserAgent);
    }

    #endregion
}
