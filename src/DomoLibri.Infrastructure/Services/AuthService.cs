using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
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
    private readonly IEmailService _emailService;

    public AuthService(DomoLibriDbContext context, IConfiguration configuration, IEmailService emailService)
    {
        _context = context;
        _configuration = configuration;
        _emailService = emailService;
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

        var editora = new Editora
        {
            Id = Guid.NewGuid(),
            Nome = dto.NomeEditora,
            Slug = slug,
            DataCriacao = DateTime.UtcNow,
            Ativo = true
        };

        var senhaHash = BCrypt.Net.BCrypt.HashPassword(dto.Senha);
        var confirmationToken = Guid.NewGuid().ToString("N");

        var adminUser = new UsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = editora.Id,
            Email = email,
            SenhaHash = senhaHash,
            Nome = dto.NomeAdmin,
            Role = Role.Admin,
            Ativo = true,
            EmailConfirmado = false,
            TokenConfirmacao = confirmationToken,
            ExpiracaoToken = DateTime.UtcNow.AddHours(24)
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
        var user = await _context.UsuariosEditora
            .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower());

        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Senha, user.SenhaHash))
            throw new UnauthorizedAccessException("Credenciais inválidas.");

        if (!user.Ativo)
            throw new UnauthorizedAccessException("Usuário inativo.");

        if (!user.EmailConfirmado)
            throw new UnauthorizedAccessException("E-mail não verificado.");

        var token = GenerateJwt(user);
        return new LoginResult(token);
    }

    public async Task VerifyEmailAsync(VerifyEmailDto dto)
    {
        var user = await _context.UsuariosEditora
            .FirstOrDefaultAsync(u => u.Email == dto.Email.Trim().ToLower());

        if (user is null || user.TokenConfirmacao != dto.Token)
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

    private string GenerateJwt(UsuarioEditora user)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var secret = jwtSection["Secret"]!;
        var issuer = jwtSection["Issuer"];
        var audience = jwtSection["Audience"];
        var expiresInHours = int.Parse(jwtSection["ExpiresInHours"] ?? "8");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("tenant_id", user.EditoraId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiresInHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string GerarSlug(string nome)
    {
        var slug = nome.ToLowerInvariant().Trim();

        slug = Regex.Replace(slug, @"[àáâãäå]", "a");
        slug = Regex.Replace(slug, @"[èéêë]", "e");
        slug = Regex.Replace(slug, @"[ìíîï]", "i");
        slug = Regex.Replace(slug, @"[òóôõö]", "o");
        slug = Regex.Replace(slug, @"[ùúûü]", "u");
        slug = Regex.Replace(slug, @"[ç]", "c");
        slug = Regex.Replace(slug, @"[ñ]", "n");
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-").Trim('-');

        return slug;
    }
}
