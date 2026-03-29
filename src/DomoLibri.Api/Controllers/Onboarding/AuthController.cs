using System.ComponentModel.DataAnnotations;
using DomoLibri.Api;
using DomoLibri.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
[EnableRateLimiting("auth-limit")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Step 1: Registers a new Editora and its first Admin user.
    /// Sets a secure HttpOnly cookie with the JWT.
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

            SetTokenCookie(result.Token);

            return Created(
                $"/api/editoras/{result.EditoraId}",
                new { result.EditoraId, Token = result.Token, Message = "Editora criada com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                instance: HttpContext.Request.Path,
                statusCode: StatusCodes.Status409Conflict,
                title: ProblemDetailsHelper.GetTitle(409),
                type: ProblemDetailsHelper.GetTypeUri(409));
        }
    }

    /// <summary>
    /// Step 2: Authenticates a user and sets a secure HttpOnly cookie with the JWT.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var dto = new LoginDto(request.Email, request.Senha);
            var result = await _authService.LoginAsync(dto);
            
            SetTokenCookie(result.Token);
            
            return Ok(new { result.Token, Message = "Login realizado com sucesso." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Problem(
                detail: ex.Message,
                instance: HttpContext.Request.Path,
                statusCode: StatusCodes.Status401Unauthorized,
                title: ProblemDetailsHelper.GetTitle(401),
                type: ProblemDetailsHelper.GetTypeUri(401));
        }
    }

    /// <summary>
    /// Clears the authentication cookie.
    /// </summary>
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("access_token");
        return Ok(new { Message = "Logout realizado com sucesso." });
    }

    private void SetTokenCookie(string token)
    {
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true, // Must be true for SameSite=None or when using HTTPS
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddHours(8)
        };
        Response.Cookies.Append("access_token", token, cookieOptions);
    }
}
