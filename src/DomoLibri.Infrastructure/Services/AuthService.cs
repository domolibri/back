using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DomoLibri.Application.Services;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace DomoLibri.Infrastructure.Services;

public class AuthService : IAuthService
{
    private readonly DomoLibriDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly INotificationService _notificationService;
    private readonly ITenantSetupService _tenantSetupService;
    private readonly IUserContextProvider _userContextProvider;
    private readonly IUsuarioRepository _usuarioRepository;
    private readonly IEditoraRepository _editoraRepository;

    public AuthService(
        DomoLibriDbContext context,
        IConfiguration configuration,
        INotificationService notificationService,
        ITenantSetupService tenantSetupService,
        IUserContextProvider userContextProvider,
        IUsuarioRepository usuarioRepository,
        IEditoraRepository editoraRepository)
    {
        _context = context;
        _configuration = configuration;
        _notificationService = notificationService;
        _tenantSetupService = tenantSetupService;
        _userContextProvider = userContextProvider;
        _usuarioRepository = usuarioRepository;
        _editoraRepository = editoraRepository;
    }

    private const string VersaoTermosDeUso = "1.0";

    public async Task<RegisterEditoraResult> RegisterAsync(RegisterEditoraDto dto)
    {
        if (!dto.AceitouTermos)
            throw new InvalidOperationException("É necessário aceitar os Termos de Uso para concluir o cadastro.");

        var email = dto.EmailAdmin.Trim().ToLower();

        // 1. Check if Editora name/slug is already taken
        var slugExiste = await _tenantSetupService.SlugExisteAsync(dto.NomeEditora);
        if (slugExiste)
            throw new InvalidOperationException($"Já existe uma editora com o nome '{dto.NomeEditora}'.");

        var slug = _tenantSetupService.GerarSlug(dto.NomeEditora);

        // 2. Check if Email is already registered globally
        var existingUser = await _usuarioRepository.FindByEmailAsync(email);

        if (existingUser != null)
        {
            // Existing global identity → create a new Editora and bind the user to it.
            var novaEditora = new Editora(dto.NomeEditora, slug);

            var adminRoleNovo = await _tenantSetupService.SeedDefaultRolesAsync(novaEditora.Id);

            var novoVinculo = new VinculoUsuarioEditora
            {
                Id = Guid.NewGuid(),
                EditoraId = novaEditora.Id,
                UsuarioId = existingUser.Id,
                Ativo = true,
                DataEntrada = DateTime.UtcNow,
                TipoVinculo = TipoVinculo.Administrador,
                Roles = [adminRoleNovo]
            };

            var novoConsentimento = new ConsentimentoLGPD
            {
                Id = Guid.NewGuid(),
                EditoraId = novaEditora.Id,
                UsuarioId = novoVinculo.Id,
                TipoConsentimento = "TermosDeUso",
                VersaoTermo = VersaoTermosDeUso,
                DataConsentimento = DateTime.UtcNow
            };

            _context.Editoras.Add(novaEditora);
            _context.VinculosUsuarioEditora.Add(novoVinculo);
            _context.ConsentimentosLGPD.Add(novoConsentimento);
            await _context.SaveChangesAsync();

            await _notificationService.SendEditoraLinkedEmailAsync(existingUser.Email, existingUser.Nome, dto.NomeEditora);

            return new RegisterEditoraResult(novaEditora.Id);
        }

        var editora = new Editora(dto.NomeEditora, slug);

        var senhaHash = BCrypt.Net.BCrypt.HashPassword(dto.Senha);
        var confirmationToken = GenerateSecureToken();
        var hashedConfirmationToken = HashToken(confirmationToken);

        var adminRole = await _tenantSetupService.SeedDefaultRolesAsync(editora.Id);

        // 3. Create the global user identity
        var usuario = new Usuario(email, senhaHash, dto.NomeAdmin);
        usuario.DefinirTokenConfirmacao(hashedConfirmationToken, DateTime.UtcNow.AddHours(24));

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

        await _notificationService.SendVerificationEmailAsync(usuario.Email, usuario.Nome, confirmationToken);

        return new RegisterEditoraResult(editora.Id);
    }

    public async Task<LoginResult> LoginAsync(LoginDto dto)
    {
        Usuario? usuario = null;
        VinculoUsuarioEditora? vinculo = null;
        try
        {
            usuario = await _usuarioRepository.FindByEmailAsync(dto.Email);

            if (usuario is null)
                throw new UnauthorizedAccessException("Credenciais inválidas.");

            if (!usuario.EmailConfirmado)
                throw new UnauthorizedAccessException("E-mail não verificado.");

            // 1. Load ALL active bindings early — needed for context list and audit logging on failure
            var vinculos = await _context.VinculosUsuarioEditora
                .IgnoreQueryFilters()
                .Include(v => v.Roles)
                .Include(v => v.Editora)
                .Where(v => v.UsuarioId == usuario.Id && v.Ativo)
                .ToListAsync();

            vinculo = vinculos.FirstOrDefault();

            // 2. Check if the account is currently locked
            if (usuario.EstaBloqueado())
            {
                var remainingTime = Math.Ceiling((usuario.BloqueioAte!.Value - DateTime.UtcNow).TotalMinutes);
                throw new UnauthorizedAccessException($"Esta conta está temporariamente bloqueada por múltiplas tentativas falhas. Tente novamente em {remainingTime} minuto(s).");
            }

            // 3. Verify password
            if (!BCrypt.Net.BCrypt.Verify(dto.Senha, usuario.SenhaHash))
            {
                usuario.RegistrarAcessoFalho();

                await _context.SaveChangesAsync();
                throw new UnauthorizedAccessException("Credenciais inválidas.");
            }

            // 4. Require at least one active binding
            if (vinculos.Count == 0)
                throw new UnauthorizedAccessException("Usuário sem vínculo ativo com uma editora.");

            // 5. Reset lockout state on successful credential validation
            if (usuario.AcessosFalhos > 0 || usuario.BloqueioAte.HasValue) usuario.ResetarBloqueio();

            var contextos = vinculos
                .Select(v => new ContextoDisponivel(
                    v.Id,
                    v.EditoraId,
                    v.Editora?.Nome ?? "",
                    v.Roles.Select(r => r.Nome).ToList()))
                .ToList();

            if (_context.ChangeTracker.HasChanges())
                await _context.SaveChangesAsync();

            return new LoginResult(usuario.Id, usuario.Nome, usuario.Email, contextos);
        }
        catch (UnauthorizedAccessException)
        {
            await AddAuthAuditLogAsync("LoginFalha", usuario, vinculo, dto.Email);
            throw;
        }
    }

    public async Task<SelectContextResult> SelectContextAsync(SelectContextDto dto)
    {
        var vinculo = await _context.VinculosUsuarioEditora
            .IgnoreQueryFilters()
            .Include(v => v.Roles)
            .FirstOrDefaultAsync(v => v.UsuarioId == dto.UsuarioId && v.EditoraId == dto.EditoraId && v.Ativo);

        if (vinculo is null)
            throw new UnauthorizedAccessException("Vínculo com a editora não encontrado ou inativo.");

        var usuario = await _usuarioRepository.FindByIdAsync(dto.UsuarioId);

        if (usuario is null)
            throw new UnauthorizedAccessException("Usuário não encontrado.");

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
        return new SelectContextResult(token);
    }

    public async Task VerifyEmailAsync(VerifyEmailDto dto)
    {
        var usuario = await _usuarioRepository.FindByEmailAsync(dto.Email);

        if (usuario is null || usuario.TokenConfirmacao != HashToken(dto.Token))
            throw new InvalidOperationException("Token de verificação inválido.");

        if (usuario.ExpiracaoToken < DateTime.UtcNow)
            throw new InvalidOperationException("Token de verificação expirado.");

        if (usuario.EmailConfirmado)
            throw new InvalidOperationException("E-mail já confirmado.");

        usuario.ConfirmarEmail();

        await _context.SaveChangesAsync();
    }

    public async Task ResendVerificationEmailAsync(string email)
    {
        var usuario = await _usuarioRepository.FindByEmailAsync(email);

        if (usuario is null || usuario.EmailConfirmado)
            return;

        var newToken = GenerateSecureToken();
        usuario.DefinirTokenConfirmacao(HashToken(newToken), DateTime.UtcNow.AddHours(24));

        await _context.SaveChangesAsync();

        await _notificationService.SendVerificationEmailAsync(usuario.Email, usuario.Nome, newToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordDto dto)
    {
        var usuario = await _usuarioRepository.FindByEmailAsync(dto.Email);

        if (usuario is null || !usuario.EmailConfirmado)
            return;

        var resetToken = GenerateSecureToken();
        usuario.DefinirTokenRedefinicaoSenha(HashToken(resetToken), DateTime.UtcNow.AddHours(1));

        await _context.SaveChangesAsync();

        await _notificationService.SendPasswordResetEmailAsync(usuario.Email, usuario.Nome, resetToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordDto dto)
    {
        var usuario = await _usuarioRepository.FindByEmailAsync(dto.Email);

        if (usuario is null)
            throw new InvalidOperationException("Link de redefinição inválido.");

        if (usuario.TokenRedefinicaoSenha is null && usuario.SenhaAlteradaEm.HasValue)
            throw new InvalidOperationException("Este link já foi utilizado. Solicite um novo link se necessário.");

        if (usuario.TokenRedefinicaoSenha != HashToken(dto.Token))
            throw new InvalidOperationException("Link de redefinição inválido.");

        if (usuario.ExpiracaoTokenRedefinicaoSenha < DateTime.UtcNow)
            throw new InvalidOperationException("Link de redefinição expirado. Solicite um novo.");

        usuario.RedefinirSenha(BCrypt.Net.BCrypt.HashPassword(dto.NovaSenha));

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
                RecursoId = usuario?.Email?.Value ?? emailTentativa ?? "",
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
}
