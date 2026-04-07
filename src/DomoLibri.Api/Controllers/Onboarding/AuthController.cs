using System.ComponentModel.DataAnnotations;
using DomoLibri.Api;
using DomoLibri.Application.Services;
using DomoLibri.Infrastructure.Data;
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
    string NomeAdmin,

    [Range(typeof(bool), "true", "true", ErrorMessage = "É necessário aceitar os Termos de Uso para prosseguir.")]
    bool AceitouTermos);

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
    [RegularExpression(@"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$",
        ErrorMessage = "A senha deve conter pelo menos uma letra maiúscula, uma minúscula, um número e um caractere especial.")]
    string NovaSenha);

public record SelectContextRequest(
    [Required(ErrorMessage = "EditoraId é obrigatório.")]
    Guid EditoraId);



[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth-limit")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly DomoLibriDbContext _db;

    public AuthController(IAuthService authService, DomoLibriDbContext db)
    {
        _authService = authService;
        _db = db;
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
                request.NomeAdmin,
                request.AceitouTermos);

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
    /// Step 3: Validates credentials and returns the list of available editora contexts.
    /// Sets a short-lived pre-auth cookie. Call /select-context to obtain a scoped JWT.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var dto = new LoginDto(request.Email, request.Senha);
            var result = await _authService.LoginAsync(dto);

            // Short-lived cookie so /select-context can identify the user without re-sending credentials
            Response.Cookies.Append("pre_auth_user", result.UsuarioId.ToString(), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Expires = DateTime.UtcNow.AddMinutes(5)
            });

            return Ok(new
            {
                result.UsuarioId,
                result.Nome,
                result.Email,
                result.Contextos,
                Message = "Credenciais validadas. Selecione o contexto da editora."
            });
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
    /// Step 4: Selects an editora context and issues a scoped JWT.
    /// Requires the pre_auth_user cookie set by /login.
    /// </summary>
    [HttpPost("select-context")]
    public async Task<IActionResult> SelectContext([FromBody] SelectContextRequest request)
    {
        try
        {
            var preAuthCookie = Request.Cookies["pre_auth_user"];
            if (!Guid.TryParse(preAuthCookie, out var usuarioId))
                return Problem(
                    detail: "Sessão inválida ou expirada. Faça login novamente.",
                    instance: HttpContext.Request.Path,
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: ProblemDetailsHelper.GetTitle(401),
                    type: ProblemDetailsHelper.GetTypeUri(401));

            var dto = new SelectContextDto(usuarioId, request.EditoraId);
            var result = await _authService.SelectContextAsync(dto);

            SetTokenCookie(result.Token);
            Response.Cookies.Delete("pre_auth_user");

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
    /// Validates the current session cookie and returns the authenticated user's identity.
    /// Used by the frontend on startup to restore auth state without re-login.
    /// </summary>
    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Me()
    {
        var tenantId = User.FindFirst("tenant_id")?.Value;
        var email    = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? User.FindFirst("email")?.Value;
        var nome     = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                    ?? User.FindFirst("name")?.Value;

        var brandingConfigurado = false;
        string? nomeEditora = null;
        string? logoUrl = null;
        string? corPrimaria = null;
        if (Guid.TryParse(tenantId, out var editoraId))
        {
            var editora = await _db.Editoras.FindAsync(editoraId);
            if (editora is not null)
            {
                nomeEditora = editora.Nome;
                logoUrl = editora.LogoUrl;
                corPrimaria = editora.CorPrimaria;
                brandingConfigurado = logoUrl is not null || corPrimaria is not null;
            }
        }

        var permissions = User.Claims
            .Where(c => c.Type == "permission")
            .Select(c => c.Value)
            .ToList();

        var roles = User.Claims
            .Where(c => c.Type == "role")
            .Select(c => c.Value)
            .ToList();

        return Ok(new { tenantId, email, nome, nomeEditora, brandingConfigurado, logoUrl, corPrimaria, permissions, roles });
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
