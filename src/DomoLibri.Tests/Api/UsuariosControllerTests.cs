using DomoLibri.Api.Controllers;
using DomoLibri.Application.Services;
using DomoLibri.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace DomoLibri.Tests.Api;

public class UsuariosControllerTests
{
    private static (UsuariosController controller, Mock<IUsuarioService> serviceMock, Mock<ITenantProvider> tenantProvider) CreateController(Guid? tenantId = null)
    {
        var serviceMock = new Mock<IUsuarioService>();
        var tenantProvider = new Mock<ITenantProvider>();
        tenantProvider.Setup(t => t.GetTenantId()).Returns(tenantId);

        var controller = new UsuariosController(serviceMock.Object, tenantProvider.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        controller.ControllerContext.HttpContext.Request.Path = "/api/usuarios";

        return (controller, serviceMock, tenantProvider);
    }

    [Fact]
    public async Task GetUsuarios_WithTenant_ReturnsOk()
    {
        var tenantId = Guid.NewGuid();
        var (controller, serviceMock, _) = CreateController(tenantId);
        serviceMock.Setup(s => s.ListarUsuariosAsync(tenantId))
            .ReturnsAsync([new VinculoUsuarioResponseDto(Guid.NewGuid(), "Maria", "maria@editora.com", "Interno", "Ativo", DateTime.UtcNow)]);

        var result = await controller.GetUsuarios();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
    }

    [Fact]
    public async Task GetUsuarios_WithoutTenant_ReturnsUnauthorized()
    {
        var (controller, _, _) = CreateController();

        var result = await controller.GetUsuarios();

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task PostUsuario_ValidRequest_ReturnsCreated()
    {
        var tenantId = Guid.NewGuid();
        var (controller, serviceMock, _) = CreateController(tenantId);
        var dto = new CriarUsuarioDto("Maria", "maria@editora.com", "Interno");
        serviceMock.Setup(s => s.CadastrarOuVincularUsuarioAsync(dto, tenantId))
            .ReturnsAsync(new VinculoUsuarioResponseDto(Guid.NewGuid(), "Maria", "maria@editora.com", "Interno", "Ativo", DateTime.UtcNow));

        var result = await controller.PostUsuario(dto);

        var created = Assert.IsType<CreatedResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
    }

    [Fact]
    public async Task PostUsuario_ServiceConflict_ReturnsProblemDetails()
    {
        var tenantId = Guid.NewGuid();
        var (controller, serviceMock, _) = CreateController(tenantId);
        var dto = new CriarUsuarioDto("Maria", "maria@editora.com", "Interno");
        serviceMock.Setup(s => s.CadastrarOuVincularUsuarioAsync(dto, tenantId))
            .ThrowsAsync(new InvalidOperationException("Usuário já possui vínculo com esta editora."));

        var result = await controller.PostUsuario(dto);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }
}
