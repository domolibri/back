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
    [MinLength(8, ErrorMessage = "Senha deve ter pelo menos 8 caracteres.")]
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$", 
        ErrorMessage = "A senha deve conter pelo menos uma letra maiúscula, uma minúscula, um número e um caractere especial.")]
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

public record VerifyEmailRequest(
    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    string Email,

    [Required(ErrorMessage = "Token é obrigatório.")]
    string Token);

public record ResendVerificationEmailRequest(
    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    string Email);

public record ForgotPasswordRequest(
    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    string Email);

public record ResetPasswordRequest(
    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    string Email,

    [Required(ErrorMessage = "Token é obrigatório.")]
    string Token,

    [Required(ErrorMessage = "Nova senha é obrigatória.")]
    [MinLength(8, ErrorMessage = "Senha deve ter pelo menos 8 caracteres.")]
    string NovaSenha);

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
    /// Does NOT set a cookie yet. User must verify e-mail first.
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

            // Removed SetTokenCookie here. Security: Force verification before login.

            return Created(
                $"/api/editoras/{result.EditoraId}",
                new { result.EditoraId, Message = "Editora criada com sucesso. Verifique seu e-mail para confirmar a conta." });
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
    /// Step 2: Verifies the user's e-mail using the token sent during registration.
    /// </summary>
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
    {
        try
        {
            var dto = new VerifyEmailDto(request.Email, request.Token);
            await _authService.VerifyEmailAsync(dto);

            return Ok(new { Message = "E-mail verificado com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                instance: HttpContext.Request.Path,
                statusCode: StatusCodes.Status400BadRequest,
                title: ProblemDetailsHelper.GetTitle(400),
                type: ProblemDetailsHelper.GetTypeUri(400));
        }
    }

    /// <summary>
    /// Step 3: Authenticates a user and sets a secure HttpOnly cookie with the JWT.
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
    /// Resends the e-mail verification link for an unverified account.
    /// Always returns 200 to avoid e-mail enumeration.
    /// </summary>
    [HttpPost("resend-verification-email")]
    public async Task<IActionResult> ResendVerificationEmail([FromBody] ResendVerificationEmailRequest request)
    {
        await _authService.ResendVerificationEmailAsync(request.Email);
        return Ok(new { Message = "Se o e-mail existir e não estiver verificado, um novo link foi enviado." });
    }

    /// <summary>
    /// Sends a password reset link to the given e-mail.
    /// Always returns 200 to avoid e-mail enumeration.
    /// </summary>
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await _authService.ForgotPasswordAsync(new ForgotPasswordDto(request.Email));
        return Ok(new { Message = "Se o e-mail estiver cadastrado, você receberá um link em breve." });
    }

    /// <summary>
    /// Resets the user's password using a valid reset token.
    /// </summary>
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        try
        {
            await _authService.ResetPasswordAsync(new ResetPasswordDto(request.Email, request.Token, request.NovaSenha));
            return Ok(new { Message = "Senha redefinida com sucesso." });
        }
        catch (InvalidOperationException ex)
        {
            return Problem(
                detail: ex.Message,
                instance: HttpContext.Request.Path,
                statusCode: StatusCodes.Status400BadRequest,
                title: ProblemDetailsHelper.GetTitle(400),
                type: ProblemDetailsHelper.GetTypeUri(400));
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
