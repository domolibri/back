using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DomoLibri.Application.Services;
using DomoLibri.Domain;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
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

    private const string VersaoTermosDeUso = "1.0";

    public async Task<RegisterEditoraResult> RegisterAsync(RegisterEditoraDto dto)
    {
        if (!dto.AceitouTermos)
            throw new InvalidOperationException("É necessário aceitar os Termos de Uso para concluir o cadastro.");
        var email = dto.EmailAdmin.Trim().ToLower();

        // 1. Check if Editora name/slug is already taken
        var slug = GerarSlug(dto.NomeEditora);

        var slugExiste = await _context.Editoras
            .AnyAsync(e => e.Slug == slug);

        if (slugExiste)
            throw new InvalidOperationException($"Já existe uma editora com o nome '{dto.NomeEditora}'.");

        // 2. Check if Email is already registered globally
        var existingUser = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == email);

        if (existingUser != null)
        {
            // Security: To avoid email enumeration, we return success even if email exists.
            // But we send an email to the user informing them of the attempt.
            await _emailService.SendEmailAsync(email, "Tentativa de cadastro",
                $"Olá {existingUser.Nome}, alguém tentou cadastrar uma nova editora com seu e-mail. Se foi você, lembre-se que já possui uma conta.");

            // Return a dummy EditoraId to avoid info leakage
            return new RegisterEditoraResult(Guid.Empty);
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

        // 3. Create the global user identity
        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Email = email,
            SenhaHash = senhaHash,
            Nome = dto.NomeAdmin,
            EmailConfirmado = false,
            TokenConfirmacao = hashedConfirmationToken,
            ExpiracaoToken = DateTime.UtcNow.AddHours(24)
        };

        // 4. Create the per-tenant binding
        var vinculo = new VinculoUsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = editora.Id,
            UsuarioId = usuario.Id,
            Ativo = true,
            DataEntrada = DateTime.UtcNow,
            TipoVinculo = TipoVinculo.Administrador,
            Roles = [adminRole]
        };

        var consentimento = new ConsentimentoLGPD
        {
            Id = Guid.NewGuid(),
            EditoraId = editora.Id,
            UsuarioId = vinculo.Id,
            TipoConsentimento = "TermosDeUso",
            VersaoTermo = VersaoTermosDeUso,
            DataConsentimento = DateTime.UtcNow
        };

        _context.Editoras.Add(editora);
        _context.Usuarios.Add(usuario);
        _context.VinculosUsuarioEditora.Add(vinculo);
        _context.ConsentimentosLGPD.Add(consentimento);
        await _context.SaveChangesAsync();

        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var verificationLink = $"{frontendUrl}/onboarding/verify-email" +
                               $"?email={Uri.EscapeDataString(usuario.Email)}&token={confirmationToken}";

        await _emailService.SendVerificationEmailAsync(usuario.Email, usuario.Nome, verificationLink);

        return new RegisterEditoraResult(editora.Id);
    }

    public async Task<LoginResult> LoginAsync(LoginDto dto)
    {
        Usuario? usuario = null;
        VinculoUsuarioEditora? vinculo = null;
        try
        {
            usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower());

            if (usuario is null)
                throw new UnauthorizedAccessException("Credenciais inválidas.");

            if (!usuario.EmailConfirmado)
                throw new UnauthorizedAccessException("E-mail não verificado.");

            // 1. Load the active tenant binding early so it's available for audit logging on any failure
            vinculo = await _context.VinculosUsuarioEditora
                .IgnoreQueryFilters()
                .Include(v => v.Roles)
                .FirstOrDefaultAsync(v => v.UsuarioId == usuario.Id && v.Ativo);

            // 2. Check if the account is currently locked
            if (usuario.BloqueioAte.HasValue && usuario.BloqueioAte.Value > DateTime.UtcNow)
            {
                var remainingTime = Math.Ceiling((usuario.BloqueioAte.Value - DateTime.UtcNow).TotalMinutes);
                throw new UnauthorizedAccessException($"Esta conta está temporariamente bloqueada por múltiplas tentativas falhas. Tente novamente em {remainingTime} minuto(s).");
            }

            // 3. Verify password
            if (!BCrypt.Net.BCrypt.Verify(dto.Senha, usuario.SenhaHash))
            {
                usuario.AcessosFalhos++;

                if (usuario.AcessosFalhos >= 5)
                {
                    usuario.BloqueioAte = DateTime.UtcNow.AddMinutes(15);
                    usuario.AcessosFalhos = 0;
                }

                await _context.SaveChangesAsync();
                throw new UnauthorizedAccessException("Credenciais inválidas.");
            }

            // 4. Check binding exists
            if (vinculo is null)
                throw new UnauthorizedAccessException("Usuário sem vínculo ativo com uma editora.");

            // 5. Reset lockout state on successful login
            if (usuario.AcessosFalhos > 0 || usuario.BloqueioAte.HasValue)
            {
                usuario.AcessosFalhos = 0;
                usuario.BloqueioAte = null;
                await _context.SaveChangesAsync();
            }

            // 5. Load unique permission codes from all the user's roles
            IReadOnlyList<string> permCodes = [];
            if (vinculo.Roles.Count > 0)
            {
                var roleIds = vinculo.Roles.Select(r => r.Id).ToList();
                permCodes = await _context.Roles
                    .IgnoreQueryFilters()
                    .Where(r => roleIds.Contains(r.Id))
                    .SelectMany(r => r.Permissions)
                    .Select(p => p.Codigo)
                    .Distinct()
                    .ToListAsync();
            }

            var token = GenerateJwt(usuario, vinculo, permCodes);
            await AddAuthAuditLogAsync("LoginSucesso", usuario, vinculo);
            return new LoginResult(token);
        }
        catch (UnauthorizedAccessException)
        {
            await AddAuthAuditLogAsync("LoginFalha", usuario, vinculo, dto.Email);
            throw;
        }
    }

    public async Task VerifyEmailAsync(VerifyEmailDto dto)
    {
        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower());

        if (usuario is null || usuario.TokenConfirmacao != HashToken(dto.Token))
            throw new InvalidOperationException("Token de verificação inválido.");

        if (usuario.ExpiracaoToken < DateTime.UtcNow)
            throw new InvalidOperationException("Token de verificação expirado.");

        if (usuario.EmailConfirmado)
            throw new InvalidOperationException("E-mail já confirmado.");

        usuario.EmailConfirmado = true;
        usuario.TokenConfirmacao = null;
        usuario.ExpiracaoToken = null;

        await _context.SaveChangesAsync();
    }

    public async Task ResendVerificationEmailAsync(string email)
    {
        var normalizedEmail = email.Trim().ToLower();

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail);

        if (usuario is null || usuario.EmailConfirmado)
            return;

        var newToken = GenerateSecureToken();
        usuario.TokenConfirmacao = HashToken(newToken);
        usuario.ExpiracaoToken = DateTime.UtcNow.AddHours(24);

        await _context.SaveChangesAsync();

        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var verificationLink = $"{frontendUrl}/onboarding/verify-email" +
                               $"?email={Uri.EscapeDataString(usuario.Email)}&token={newToken}";

        await _emailService.SendVerificationEmailAsync(usuario.Email, usuario.Nome, verificationLink);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == email);

        if (usuario is null || !usuario.EmailConfirmado)
            return;

        var resetToken = GenerateSecureToken();
        usuario.TokenRedefinicaoSenha = HashToken(resetToken);
        usuario.ExpiracaoTokenRedefinicaoSenha = DateTime.UtcNow.AddHours(1);

        await _context.SaveChangesAsync();

        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var resetLink = $"{frontendUrl}/redefinir-senha" +
                        $"?email={Uri.EscapeDataString(usuario.Email)}&token={resetToken}";

        await _emailService.SendPasswordResetEmailAsync(usuario.Email, usuario.Nome, resetLink);
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        var email = dto.Email.Trim().ToLower();

        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == email);

        if (usuario is null)
            throw new InvalidOperationException("Link de redefinição inválido.");

        if (usuario.TokenRedefinicaoSenha is null && usuario.SenhaAlteradaEm.HasValue)
            throw new InvalidOperationException("Este link já foi utilizado. Solicite um novo link se necessário.");

        if (usuario.TokenRedefinicaoSenha != HashToken(dto.Token))
            throw new InvalidOperationException("Link de redefinição inválido.");

        if (usuario.ExpiracaoTokenRedefinicaoSenha < DateTime.UtcNow)
            throw new InvalidOperationException("Link de redefinição expirado. Solicite um novo.");

        usuario.SenhaHash = BCrypt.Net.BCrypt.HashPassword(dto.NovaSenha);
        usuario.SenhaAlteradaEm = DateTime.UtcNow;
        usuario.TokenRedefinicaoSenha = null;
        usuario.ExpiracaoTokenRedefinicaoSenha = null;

        await _context.SaveChangesAsync();

        var vinculo = await _context.VinculosUsuarioEditora
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(v => v.UsuarioId == usuario.Id && v.Ativo);

        await AddAuthAuditLogAsync("RedefinicaoSenha", usuario, vinculo);
    }

    /// <summary>
    /// Persists an authentication audit entry. Best-effort: failures never block auth operations.
    /// </summary>
    private async Task AddAuthAuditLogAsync(
        string acao,
        Usuario? usuario,
        VinculoUsuarioEditora? vinculo = null,
        string? emailTentativa = null)
    {
        try
        {
            var entry = new AuditLog
            {
                Id = Guid.NewGuid(),
                EditoraId = vinculo?.EditoraId,
                UsuarioId = vinculo?.Id ?? usuario?.Id,
                Acao = acao,
                Recurso = "Auth",
                RecursoId = usuario?.Email ?? emailTentativa ?? "",
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
        }
    }

    /// <summary>
    /// Creates the three default Roles for a new Editora and ensures all global
    /// system Permission records exist (upsert by Codigo). Returns the AdminEditora role.
    /// </summary>
    private async Task<Role> SeedDefaultRolesAsync(Guid editoraId)
    {
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

        List<Permission> Resolve(string[] codes) =>
            codes.Select(c => permissionMap[c]).ToList();

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

    private string GenerateJwt(Usuario usuario, VinculoUsuarioEditora vinculo, IReadOnlyList<string> permCodes)
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
            new Claim(JwtRegisteredClaimNames.Sub, vinculo.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
            new Claim(JwtRegisteredClaimNames.Name, usuario.Nome),
            new Claim("tenant_id", vinculo.EditoraId.ToString())
        };

        foreach (var role in vinculo.Roles)
            claims.Add(new Claim("role", role.Nome));

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
