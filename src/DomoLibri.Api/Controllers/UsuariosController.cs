using DomoLibri.Api;
using DomoLibri.Application.Services;
using DomoLibri.Domain;
using DomoLibri.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomoLibri.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsuariosController : ControllerBase
{
    private readonly IUsuarioService _usuarioService;
    private readonly ITenantProvider _tenantProvider;

    public UsuariosController(IUsuarioService usuarioService, ITenantProvider tenantProvider)
    {
        _usuarioService = usuarioService;
        _tenantProvider = tenantProvider;
    }

    /// <summary>
    /// Lista os usuários vinculados à editora atual.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = SystemPermissions.UsuariosLer)]
    [ProducesResponseType(typeof(IReadOnlyList<VinculoUsuarioResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsuarios()
    {
        var tenantId = _tenantProvider.GetTenantId();
        if (tenantId is null)
            return Unauthorized();

        var usuarios = await _usuarioService.ListarUsuariosAsync(tenantId.Value);
        return Ok(usuarios);
    }

    /// <summary>
    /// Cria um novo usuário global ou vincula um usuário existente à editora atual.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = SystemPermissions.UsuariosEscrever)]
    [ProducesResponseType(typeof(VinculoUsuarioResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PostUsuario([FromBody] CriarUsuarioDto request)
    {
        var tenantId = _tenantProvider.GetTenantId();
        if (tenantId is null)
            return Unauthorized();

        try
        {
            var result = await _usuarioService.CadastrarOuVincularUsuarioAsync(request, tenantId.Value);
            return Created($"/api/usuarios/{result.Id}", result);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                instance: HttpContext.Request.Path,
                statusCode: StatusCodes.Status409Conflict,
                title: ProblemDetailsHelper.GetTitle(StatusCodes.Status409Conflict),
                type: ProblemDetailsHelper.GetTypeUri(StatusCodes.Status409Conflict));
        }
    }
}
