using System.ComponentModel.DataAnnotations;
using DomoLibri.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace DomoLibri.Api.Controllers.Onboarding;

public record RegisterRequest(
    [Required(ErrorMessage = "Nome da editora é obrigatório.")]
    [MinLength(2, ErrorMessage = "Nome da editora deve ter pelo menos 2 caracteres.")]
    string NomeEditora,

    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    string EmailAdmin,

    [Required(ErrorMessage = "Senha é obrigatória.")]
    [MinLength(6, ErrorMessage = "Senha deve ter pelo menos 6 caracteres.")]
    string Senha,

    [Required(ErrorMessage = "Nome do administrador é obrigatório.")]
    [MinLength(2, ErrorMessage = "Nome deve ter pelo menos 2 caracteres.")]
    string NomeAdmin);

public record LoginRequest(
    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    string Email,

    [Required(ErrorMessage = "Senha é obrigatória.")]
    string Senha);

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Step 1: Registers a new Editora and its first Admin user.
    /// Endpoint: POST /api/auth/register
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var dto = new RegisterEditoraDto(
                request.NomeEditora,
                request.EmailAdmin,
                request.Senha,
                request.NomeAdmin);

            var result = await _authService.RegisterAsync(dto);

            return Created(
                $"/api/editoras/{result.EditoraId}",
                new { result.EditoraId, result.Token, Message = "Editora criada com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "Conflito.",
                type: "https://tools.ietf.org/html/rfc7807");
        }
    }

    /// <summary>
    /// Step 2: Authenticates a user and returns a JWT containing the EditoraId.
    /// Endpoint: POST /api/auth/login
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var dto = new LoginDto(request.Email, request.Senha);
            var result = await _authService.LoginAsync(dto);
            return Ok(new { result.Token });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { Message = ex.Message });
        }
    }
}