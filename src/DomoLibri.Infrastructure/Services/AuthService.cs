using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DomoLibri.Application.Services;
using DomoLibri.Domain;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace DomoLibri.Infrastructure.Services;

public partial class AuthService : IAuthService
{
    private readonly DomoLibriDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly IUserContextProvider _userContextProvider;

    public AuthService(
        DomoLibriDbContext context,
        IConfiguration configuration,
        IEmailService emailService,
        IUserContextProvider userContextProvider)
    {
        _context = context;
        _configuration = configuration;
        _emailService = emailService;
        _userContextProvider = userContextProvider;
    }

    public async Task<RegisterEditoraResult> RegisterAsync(RegisterEditoraDto dto)
    {
        var email = dto.EmailAdmin.Trim().ToLower();

        // 1. Check if Editora name/slug is already taken
        var slug = GerarSlug(dto.NomeEditora);

        var slugExiste = await _context.Editoras
            .AnyAsync(e => e.Slug == slug);

        if (slugExiste)
            throw new InvalidOperationException($"Já existe uma editora com o nome '{dto.NomeEditora}'.");

        // 2. Check if Email is already registered globally (since we don't have tenant context yet)
        var existingUser = await _context.UsuariosEditora
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (existingUser != null)
        {
            // Security: To avoid email enumeration, we return success even if email exists.
            // But we send an email to the user informing them of the attempt.
            await _emailService.SendEmailAsync(email, "Tentativa de cadastro", 
                $"Olá {existingUser.Nome}, alguém tentou cadastrar uma nova editora com seu e-mail. Se foi você, lembre-se que já possui uma conta.");
            
            // Return a dummy ID or the existing one (careful with info leakage)
            return new RegisterEditoraResult(existingUser.EditoraId);
        }

        var editora = new Editora
        {
            Id = Guid.NewGuid(),
            Nome = dto.NomeEditora,
            Slug = slug,
            DataCriacao = DateTime.UtcNow,
            Ativo = true
        };

        var senhaHash = BCrypt.Net.BCrypt.HashPassword(dto.Senha);
        var confirmationToken = GenerateSecureToken();
        var hashedConfirmationToken = HashToken(confirmationToken);

        var adminRole = await SeedDefaultRolesAsync(editora.Id);

        var adminUser = new UsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = editora.Id,
            Email = email,
            SenhaHash = senhaHash,
            Nome = dto.NomeAdmin,
            Ativo = true,
            EmailConfirmado = false,
            TokenConfirmacao = hashedConfirmationToken,
            ExpiracaoToken = DateTime.UtcNow.AddHours(24),
            Roles = [adminRole]
        };

        _context.Editoras.Add(editora);
        _context.UsuariosEditora.Add(adminUser);
        await _context.SaveChangesAsync();

        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var verificationLink = $"{frontendUrl}/onboarding/verify-email" +
                               $"?email={Uri.EscapeDataString(adminUser.Email)}&token={confirmationToken}";

        await _emailService.SendVerificationEmailAsync(adminUser.Email, adminUser.Nome, verificationLink);

        return new RegisterEditoraResult(editora.Id);
    }

    public async Task<LoginResult> LoginAsync(LoginDto dto)
    {
        UsuarioEditora? user = null;
        try
        {
            user = await _context.UsuariosEditora
                .IgnoreQueryFilters()
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower());

            if (user is null)
                throw new UnauthorizedAccessException("Credenciais inválidas.");

            if (!user.Ativo)
                throw new UnauthorizedAccessException("Usuário inativo.");

            if (!user.EmailConfirmado)
                throw new UnauthorizedAccessException("E-mail não verificado.");

            // 1. Check if the account is currently locked
            if (user.BloqueioAte.HasValue && user.BloqueioAte.Value > DateTime.UtcNow)
            {
                var remainingTime = Math.Ceiling((user.BloqueioAte.Value - DateTime.UtcNow).TotalMinutes);
                throw new UnauthorizedAccessException($"Esta conta está temporariamente bloqueada por múltiplas tentativas falhas. Tente novamente em {remainingTime} minuto(s).");
            }

            // 2. Verify password
            if (!BCrypt.Net.BCrypt.Verify(dto.Senha, user.SenhaHash))
            {
                // Increment failed attempts
                user.AcessosFalhos++;

                if (user.AcessosFalhos >= 5)
                {
                    user.BloqueioAte = DateTime.UtcNow.AddMinutes(15);
                    user.AcessosFalhos = 0; // Reset after locking
                }

                await _context.SaveChangesAsync();
                throw new UnauthorizedAccessException("Credenciais inválidas.");
            }

            // 3. Reset lockout state on successful login (only persist if there is something to clear)
            if (user.AcessosFalhos > 0 || user.BloqueioAte.HasValue)
            {
                user.AcessosFalhos = 0;
                user.BloqueioAte = null;
                await _context.SaveChangesAsync();
            }

            // 4. Load unique permission codes from all the user's roles.
            //    Done as a separate query so IgnoreQueryFilters is applied unambiguously —
            //    ThenInclude on N:N skip navigations can silently re-apply global filters.
            IReadOnlyList<string> permCodes = [];
            if (user.Roles.Count > 0)
            {
                var roleIds = user.Roles.Select(r => r.Id).ToList();
                permCodes = await _context.Roles
                    .IgnoreQueryFilters()
                    .Where(r => roleIds.Contains(r.Id))
                    .SelectMany(r => r.Permissions)
                    .Select(p => p.Codigo)
                    .Distinct()
                    .ToListAsync();
            }

            var token = GenerateJwt(user, permCodes);
            await AddAuthAuditLogAsync("LoginSucesso", user);
            return new LoginResult(token);
        }
        catch (UnauthorizedAccessException)
        {
            await AddAuthAuditLogAsync("LoginFalha", user, dto.Email);
            throw;
        }
    }

    public async Task VerifyEmailAsync(VerifyEmailDto dto)
    {
        var user = await _context.UsuariosEditora
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower());

        if (user is null || user.TokenConfirmacao != HashToken(dto.Token))
            throw new InvalidOperationException("Token de verificação inválido.");

        if (user.ExpiracaoToken < DateTime.UtcNow)
            throw new InvalidOperationException("Token de verificação expirado.");

        if (user.EmailConfirmado)
            throw new InvalidOperationException("E-mail já confirmado.");

        user.EmailConfirmado = true;
        user.TokenConfirmacao = null;
        user.ExpiracaoToken = null;

        await _context.SaveChangesAsync();
    }

    public async Task ResendVerificationEmailAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLower();

        var user = await _context.UsuariosEditora
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        // Return silently if user not found to avoid email enumeration
        if (user is null || user.EmailConfirmado)
            return;

        var newToken = GenerateSecureToken();
        user.TokenConfirmacao = HashToken(newToken);
        user.ExpiracaoToken = DateTime.UtcNow.AddHours(24);

        await _context.SaveChangesAsync();

        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var verificationLink = $"{frontendUrl}/onboarding/verify-email" +
                               $"?email={Uri.EscapeDataString(user.Email)}&token={newToken}";

        await _emailService.SendVerificationEmailAsync(user.Email, user.Nome, verificationLink);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        var user = await _context.UsuariosEditora
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        // Return silently to avoid e-mail enumeration
        if (user is null || !user.Ativo || !user.EmailConfirmado)
            return;

        var resetToken = GenerateSecureToken();
        user.TokenRedefinicaoSenha = HashToken(resetToken);
        user.ExpiracaoTokenRedefinicaoSenha = DateTime.UtcNow.AddHours(1);

        await _context.SaveChangesAsync();

        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var resetLink = $"{frontendUrl}/redefinir-senha" +
                        $"?email={Uri.EscapeDataString(user.Email)}&token={resetToken}";

        await _emailService.SendPasswordResetEmailAsync(user.Email, user.Nome, resetLink);
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        var user = await _context.UsuariosEditora
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == email);

        if (user is null)
            throw new InvalidOperationException("Link de redefinição inválido.");

        // Explicit check: link was already consumed (token cleared after successful use)
        if (user.TokenRedefinicaoSenha is null && user.SenhaAlteradaEm.HasValue)
            throw new InvalidOperationException("Este link já foi utilizado. Solicite um novo link se necessário.");

        if (user.TokenRedefinicaoSenha != HashToken(dto.Token))
            throw new InvalidOperationException("Link de redefinição inválido.");

        if (user.ExpiracaoTokenRedefinicaoSenha < DateTime.UtcNow)
            throw new InvalidOperationException("Link de redefinição expirado. Solicite um novo.");

        user.SenhaHash = BCrypt.Net.BCrypt.HashPassword(dto.NovaSenha);
        user.SenhaAlteradaEm = DateTime.UtcNow;
        user.TokenRedefinicaoSenha = null;
        user.ExpiracaoTokenRedefinicaoSenha = null;

        await _context.SaveChangesAsync();
        await AddAuthAuditLogAsync("RedefinicaoSenha", user);
    }

    /// <summary>
    /// Persists an authentication audit entry.
    /// <paramref name="emailTentativa"/> is used when <paramref name="user"/> is null (e.g., user-not-found case).
    /// Best-effort: failures are swallowed so that auth operations are never blocked by logging issues.
    /// </summary>
    private async Task AddAuthAuditLogAsync(string acao, UsuarioEditora? user, string? emailTentativa = null)
    {
        try
        {
            var entry = new AuditLog
            {
                Id = Guid.NewGuid(),
                EditoraId = user?.EditoraId,
                UsuarioId = user?.Id,
                Acao = acao,
                Recurso = "Auth",
                RecursoId = user?.Email ?? emailTentativa ?? "",
                IP = _userContextProvider.GetIp() ?? "",
                UserAgent = _userContextProvider.GetUserAgent() ?? "",
                DataHora = DateTime.UtcNow
            };

            _context.AuditLogs.Add(entry);
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Best-effort: auth audit logging must not block auth operations.
            // TODO: forward to ILogger once injected for production observability.
        }
    }

    /// <summary>
    /// Creates the three default Roles for a new Editora and ensures all global
    /// system Permission records exist (upsert by Codigo). Returns the AdminEditora role.
    /// </summary>
    private async Task<Role> SeedDefaultRolesAsync(Guid editoraId)
    {
        // 1. Upsert global permissions — create any that are not yet in the DB.
        var allCodes = SystemPermissions.All.Select(d => d.Codigo).ToList();

        var existing = await _context.Permissions
            .Where(p => allCodes.Contains(p.Codigo))
            .ToDictionaryAsync(p => p.Codigo);

        var permissionMap = new Dictionary<string, Permission>(SystemPermissions.All.Count);
        foreach (var def in SystemPermissions.All)
        {
            if (!existing.TryGetValue(def.Codigo, out var perm))
            {
                perm = new Permission
                {
                    Id = Guid.NewGuid(),
                    Codigo = def.Codigo,
                    Nome = def.Nome,
                    Agrupamento = def.Agrupamento
                };
                _context.Permissions.Add(perm);
            }
            permissionMap[def.Codigo] = perm;
        }

        // 2. Helper: resolve a set of codes to tracked Permission entities.
        List<Permission> Resolve(string[] codes) =>
            codes.Select(c => permissionMap[c]).ToList();

        // 3. Create the three default roles for this tenant.
        var adminRole = new Role
        {
            Id = Guid.NewGuid(),
            EditoraId = editoraId,
            Nome = "AdminEditora",
            Descricao = "Acesso total ao sistema.",
            Permissions = Resolve(SystemPermissions.AdminEditoraCodes)
        };

        var gestorRole = new Role
        {
            Id = Guid.NewGuid(),
            EditoraId = editoraId,
            Nome = "GestorEditorial",
            Descricao = "Permissões de edição e visualização.",
            Permissions = Resolve(SystemPermissions.GestorEditorialCodes)
        };

        var autorRole = new Role
        {
            Id = Guid.NewGuid(),
            EditoraId = editoraId,
            Nome = "Autor",
            Descricao = "Permissões limitadas a submissão de conteúdo.",
            Permissions = Resolve(SystemPermissions.AutorCodes)
        };

        _context.Roles.Add(adminRole);
        _context.Roles.Add(gestorRole);
        _context.Roles.Add(autorRole);

        return adminRole;
    }

    private string GenerateJwt(UsuarioEditora user, IReadOnlyList<string> permCodes)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var secret = jwtSection["Secret"] ?? string.Empty;

        if (secret.Length < 32)
            throw new InvalidOperationException("JWT Secret must be at least 32 characters long.");

        var issuer = jwtSection["Issuer"];
        var audience = jwtSection["Audience"];
        var expiresInHours = int.Parse(jwtSection["ExpiresInHours"] ?? "8");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.Nome),
            new Claim("tenant_id", user.EditoraId.ToString())
        };

        // "role" short form: JwtBearerHandler maps it to ClaimTypes.Role via InboundClaimTypeMap,
        // making [Authorize(Roles = "...")] work without any extra configuration.
        foreach (var role in user.Roles)
            claims.Add(new Claim("role", role.Nome));

        // One "permission" claim per unique code — codes are short and deduplicated upstream.
        foreach (var code in permCodes)
            claims.Add(new Claim("permission", code));

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiresInHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string HashToken(string token)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    private static string GenerateSecureToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    }

    private static string GerarSlug(string nome)
    {
        var slug = nome.ToLowerInvariant().Trim();

        slug = SlugRegexA().Replace(slug, "a");
        slug = SlugRegexE().Replace(slug, "e");
        slug = SlugRegexI().Replace(slug, "i");
        slug = SlugRegexO().Replace(slug, "o");
        slug = SlugRegexU().Replace(slug, "u");
        slug = SlugRegexC().Replace(slug, "c");
        slug = SlugRegexN().Replace(slug, "n");
        slug = SlugRegexNonSlug().Replace(slug, "");
        slug = SlugRegexSpaces().Replace(slug, "-");
        slug = SlugRegexDashes().Replace(slug, "-").Trim('-');

        return slug;
    }

    [GeneratedRegex(@"[àáâãäå]")] private static partial Regex SlugRegexA();
    [GeneratedRegex(@"[èéêë]")]   private static partial Regex SlugRegexE();
    [GeneratedRegex(@"[ìíîï]")]   private static partial Regex SlugRegexI();
    [GeneratedRegex(@"[òóôõö]")]  private static partial Regex SlugRegexO();
    [GeneratedRegex(@"[ùúûü]")]   private static partial Regex SlugRegexU();
    [GeneratedRegex(@"[ç]")]      private static partial Regex SlugRegexC();
    [GeneratedRegex(@"[ñ]")]      private static partial Regex SlugRegexN();
    [GeneratedRegex(@"[^a-z0-9\s\-]")] private static partial Regex SlugRegexNonSlug();
    [GeneratedRegex(@"\s+")]      private static partial Regex SlugRegexSpaces();
    [GeneratedRegex(@"\-+")]      private static partial Regex SlugRegexDashes();
}
