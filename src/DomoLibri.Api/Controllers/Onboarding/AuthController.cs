using DomoLibri.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace DomoLibri.Api.Controllers.Onboarding;

// DTOs for the requests
public record RegisterRequest(string NomeEditora, string EmailAdmin, string Senha, string NomeAdmin);
public record LoginRequest(string Email, string Senha);

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
            return Conflict(new { Message = ex.Message });
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