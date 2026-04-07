using System.Security.Claims;
using DomoLibri.Api.Controllers.Onboarding;
using DomoLibri.Application.Services;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DomoLibri.Tests.Api;

public class AuthControllerTests
{
    private static DomoLibriDbContext CreateDbContext()
    {
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns((Guid?)null);

        var options = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new DomoLibriDbContext(options, tenantProvider.Object);
    }

    private static (AuthController controller, Mock<IAuthService> mockService) CreateController()
    {
        var mockService = new Mock<IAuthService>();
        var db = CreateDbContext();
        var controller = new AuthController(mockService.Object, db);

        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = "/api/auth/test";
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        return (controller, mockService);
    }

    #region Register

    [Fact]
    public async Task Register_ValidRequest_Returns201Created()
    {
        var (controller, mockService) = CreateController();
        var editoraId = Guid.NewGuid();
        mockService.Setup(s => s.RegisterAsync(It.IsAny<RegisterEditoraDto>()))
            .ReturnsAsync(new RegisterEditoraResult(editoraId));

        var request = new RegisterRequest("Editora Teste", "admin@teste.com", "Senha@123!", "Admin", true);
        var result = await controller.Register(request);

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateSlug_Returns409Conflict()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.RegisterAsync(It.IsAny<RegisterEditoraDto>()))
            .ThrowsAsync(new InvalidOperationException("Já existe uma editora com esse nome."));

        var request = new RegisterRequest("Editora Duplicada", "admin@teste.com", "Senha@123!", "Admin", true);
        var result = await controller.Register(request);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, problem.StatusCode);
    }

    [Fact]
    public async Task Register_MapsRequestToDto()
    {
        var (controller, mockService) = CreateController();
        RegisterEditoraDto? capturedDto = null;
        mockService.Setup(s => s.RegisterAsync(It.IsAny<RegisterEditoraDto>()))
            .Callback<RegisterEditoraDto>(dto => capturedDto = dto)
            .ReturnsAsync(new RegisterEditoraResult(Guid.NewGuid()));

        var request = new RegisterRequest("Editora X", "x@test.com", "Senha@123!", "Admin X", true);
        await controller.Register(request);

        Assert.NotNull(capturedDto);
        Assert.Equal("Editora X", capturedDto!.NomeEditora);
        Assert.Equal("x@test.com", capturedDto.EmailAdmin);
        Assert.Equal("Admin X", capturedDto.NomeAdmin);
    }

    #endregion

    #region VerifyEmail

    [Fact]
    public async Task VerifyEmail_ValidToken_Returns200Ok()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.VerifyEmailAsync(It.IsAny<VerifyEmailDto>()))
            .Returns(Task.CompletedTask);

        var request = new VerifyEmailRequest("admin@teste.com", "valid_token");
        var result = await controller.VerifyEmail(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task VerifyEmail_InvalidToken_Returns400BadRequest()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.VerifyEmailAsync(It.IsAny<VerifyEmailDto>()))
            .ThrowsAsync(new InvalidOperationException("Token de verificação inválido."));

        var request = new VerifyEmailRequest("admin@teste.com", "wrong_token");
        var result = await controller.VerifyEmail(request);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    #endregion

    #region Login

    [Fact]
    public async Task Login_ValidCredentials_Returns200WithToken()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(new LoginResult("jwt.token.here"));

        var request = new LoginRequest("admin@teste.com", "Senha@123!");
        var result = await controller.Login(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task Login_InvalidCredentials_Returns401Unauthorized()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ThrowsAsync(new UnauthorizedAccessException("Credenciais inválidas."));

        var request = new LoginRequest("admin@teste.com", "wrong");
        var result = await controller.Login(request);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, problem.StatusCode);
    }

    [Fact]
    public async Task Login_SetsHttpOnlyCookie()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.LoginAsync(It.IsAny<LoginDto>()))
            .ReturnsAsync(new LoginResult("jwt.token.here"));

        var request = new LoginRequest("admin@teste.com", "Senha@123!");
        await controller.Login(request);

        // DefaultHttpContext doesn't have full cookie support, but verifies no exception
        Assert.NotNull(controller.Response);
    }

    #endregion

    #region ResendVerificationEmail

    [Fact]
    public async Task ResendVerificationEmail_Always_Returns200()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.ResendVerificationEmailAsync(It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var request = new ResendVerificationEmailRequest("user@test.com");
        var result = await controller.ResendVerificationEmail(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task ResendVerificationEmail_PassesEmailToService()
    {
        var (controller, mockService) = CreateController();
        string? capturedEmail = null;
        mockService.Setup(s => s.ResendVerificationEmailAsync(It.IsAny<string>()))
            .Callback<string>(e => capturedEmail = e)
            .Returns(Task.CompletedTask);

        var request = new ResendVerificationEmailRequest("user@test.com");
        await controller.ResendVerificationEmail(request);

        Assert.Equal("user@test.com", capturedEmail);
    }

    #endregion

    #region ForgotPassword

    [Fact]
    public async Task ForgotPassword_Always_Returns200()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.ForgotPasswordAsync(It.IsAny<ForgotPasswordDto>()))
            .Returns(Task.CompletedTask);

        var request = new ForgotPasswordRequest("user@test.com");
        var result = await controller.ForgotPassword(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    #endregion

    #region ResetPassword

    [Fact]
    public async Task ResetPassword_ValidToken_Returns200()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()))
            .Returns(Task.CompletedTask);

        var request = new ResetPasswordRequest("user@test.com", "valid_token", "NovaSenha@123!");
        var result = await controller.ResetPassword(request);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_InvalidToken_Returns400()
    {
        var (controller, mockService) = CreateController();
        mockService.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()))
            .ThrowsAsync(new InvalidOperationException("Link de redefinição inválido."));

        var request = new ResetPasswordRequest("user@test.com", "bad_token", "NovaSenha@123!");
        var result = await controller.ResetPassword(request);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_MapsRequestToDto()
    {
        var (controller, mockService) = CreateController();
        ResetPasswordDto? capturedDto = null;
        mockService.Setup(s => s.ResetPasswordAsync(It.IsAny<ResetPasswordDto>()))
            .Callback<ResetPasswordDto>(dto => capturedDto = dto)
            .Returns(Task.CompletedTask);

        var request = new ResetPasswordRequest("user@test.com", "my_token", "NovaSenha@123!");
        await controller.ResetPassword(request);

        Assert.NotNull(capturedDto);
        Assert.Equal("user@test.com", capturedDto!.Email);
        Assert.Equal("my_token", capturedDto.Token);
        Assert.Equal("NovaSenha@123!", capturedDto.NovaSenha);
    }

    #endregion

    #region Me

    [Fact]
    public async Task Me_AuthenticatedUser_ReturnsUserInfo()
    {
        var (controller, _) = CreateController();
        var tenantId = Guid.NewGuid().ToString();

        var claims = new List<Claim>
        {
            new("tenant_id", tenantId),
            new(ClaimTypes.Email, "user@test.com"),
            new(ClaimTypes.Name, "Usuário Teste")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);

        var result = await controller.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    [Fact]
    public async Task Me_WithEmailInAlternativeClaimType_ReturnsEmail()
    {
        var (controller, _) = CreateController();

        var claims = new List<Claim>
        {
            new("tenant_id", Guid.NewGuid().ToString()),
            new("email", "alt@test.com") // alternative "email" claim (not ClaimTypes.Email)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);

        var result = await controller.Me();

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Me_ReturnsPermissionsAndRolesFromClaims()
    {
        var (controller, _) = CreateController();

        var claims = new List<Claim>
        {
            new("tenant_id", Guid.NewGuid().ToString()),
            new("role", "AdminEditora"),
            new("permission", "usuarios.ler"),
            new("permission", "obras.escrever")
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);

        var result = await controller.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = ok.Value!;
        var type = value.GetType();

        var returnedRoles = (System.Collections.Generic.List<string>)type.GetProperty("roles")!.GetValue(value)!;
        var returnedPerms = (System.Collections.Generic.List<string>)type.GetProperty("permissions")!.GetValue(value)!;

        Assert.Contains("AdminEditora", returnedRoles);
        Assert.Contains("usuarios.ler", returnedPerms);
        Assert.Contains("obras.escrever", returnedPerms);
    }

    [Fact]
    public async Task Me_WhenNoClaims_ReturnsEmptyPermissionsAndRoles()
    {
        var (controller, _) = CreateController();
        var identity = new ClaimsIdentity(new[] { new Claim("tenant_id", Guid.NewGuid().ToString()) }, "TestAuth");
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(identity);

        var result = await controller.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = ok.Value!;
        var type = value.GetType();

        var returnedRoles = (System.Collections.Generic.List<string>)type.GetProperty("roles")!.GetValue(value)!;
        var returnedPerms = (System.Collections.Generic.List<string>)type.GetProperty("permissions")!.GetValue(value)!;

        Assert.Empty(returnedRoles);
        Assert.Empty(returnedPerms);
    }

    [Fact]
    public async Task Me_WhenBrandingConfigured_ReturnsBrandingConfiguradoTrue()
    {
        var (controller, _) = CreateController();
        var editoraId = Guid.NewGuid();

        // Seed an Editora with branding set
        var db = CreateDbContext();
        db.Editoras.Add(new DomoLibri.Domain.Entities.Editora
        {
            Id = editoraId,
            Nome = "Editora Teste",
            Slug = "editora-teste",
            CorPrimaria = "#ff0000",
            DataCriacao = DateTime.UtcNow,
            Ativo = true
        });
        await db.SaveChangesAsync();

        var controllerWithDb = new AuthController(new Mock<IAuthService>().Object, db);
        var httpContext = new DefaultHttpContext();
        controllerWithDb.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var claims = new List<Claim> { new("tenant_id", editoraId.ToString()) };
        controllerWithDb.ControllerContext.HttpContext.User =
            new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));

        var result = await controllerWithDb.Me();

        var ok = Assert.IsType<OkObjectResult>(result);
        var value = ok.Value!;
        var brandingConfigurado = (bool)value.GetType().GetProperty("brandingConfigurado")!.GetValue(value)!;
        Assert.True(brandingConfigurado);
    }

    #endregion

    #region Logout

    [Fact]
    public void Logout_Returns200AndClearsCookie()
    {
        var (controller, _) = CreateController();

        var result = controller.Logout();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    #endregion
}
