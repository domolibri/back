using System.Security.Cryptography;
using DomoLibri.Application.Services;
using DomoLibri.Domain.Entities;
using DomoLibri.Domain.Enums;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace DomoLibri.Infrastructure.Services;

public class InvitationService : IInvitationService
{
    private readonly DomoLibriDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IUserContextProvider _userContextProvider;
    private readonly ITenantProvider _tenantProvider;
    private readonly IConfiguration _configuration;

    /// <summary>Default invite expiry window (48 hours).</summary>
    private const int ExpiracaoConviteHoras = 48;

    public InvitationService(
        DomoLibriDbContext context,
        IEmailService emailService,
        IUserContextProvider userContextProvider,
        ITenantProvider tenantProvider,
        IConfiguration configuration)
    {
        _context = context;
        _emailService = emailService;
        _userContextProvider = userContextProvider;
        _tenantProvider = tenantProvider;
        _configuration = configuration;
    }

    public async Task<InviteUserResult> InviteUserAsync(InviteUserDto dto)
    {
        var tenantId = _tenantProvider.GetTenantId()
            ?? throw new InvalidOperationException("Tenant não identificado. O usuário deve estar autenticado.");

        var inviterId = _userContextProvider.GetUserId()
            ?? throw new InvalidOperationException("Usuário não autenticado.");

        var email = dto.Email.Trim().ToLower();

        // 1. Guard: email must not already belong to a user with an active binding in this editora.
        //    Global query filter scopes the check to the current tenant automatically.
        var jaVinculado = await _context.VinculosUsuarioEditora
            .AnyAsync(v => v.Usuario!.Email == new DomoLibri.Domain.ValueObjects.Email(email));

        if (jaVinculado)
            throw new InvalidOperationException($"O e-mail '{email}' já pertence a um usuário desta editora.");

        // 2. Guard: no duplicate pending invite for this email within the tenant.
        var convitePendente = await _context.Convites
            .AnyAsync(c => c.Email == email && c.Status == ConviteStatus.Pendente);

        if (convitePendente)
            throw new InvalidOperationException($"Já existe um convite pendente para '{email}'.");

        // 3. Guard: role must belong to this tenant.
        var roleExiste = await _context.Roles
            .AnyAsync(r => r.Id == dto.RoleId);

        if (!roleExiste)
            throw new InvalidOperationException("A role especificada não foi encontrada nesta editora.");

        // 4. Load inviter name for the e-mail body (scoped by global filter).
        var inviter = await _context.VinculosUsuarioEditora
            .Include(v => v.Usuario)
            .FirstOrDefaultAsync(v => v.Id == inviterId)
            ?? throw new InvalidOperationException("Usuário convidante não encontrado.");

        // 5. Load editora name (Editoras has no global filter – safe to use directly).
        var editora = await _context.Editoras.FindAsync(tenantId)
            ?? throw new InvalidOperationException("Editora não encontrada.");

        // 6. Create and persist the invite (token stored in plain text for O(1) lookup;
        //    128-bit entropy makes it infeasible to brute-force within the 48h window).
        var token = GenerateInviteToken();

        var convite = new ConviteUsuario
        {
            Id = Guid.NewGuid(),
            EditoraId = tenantId,
            Email = email,
            RoleId = dto.RoleId,
            Token = token,
            DataCriacao = DateTime.UtcNow,
            DataExpiracao = DateTime.UtcNow.AddHours(ExpiracaoConviteHoras),
            Status = ConviteStatus.Pendente,
            ConvidadoPorUsuarioId = inviterId
        };

        _context.Convites.Add(convite);
        await _context.SaveChangesAsync();

        // 7. Send invite e-mail.
        var frontendUrl = _configuration["FrontendUrl"] ?? "http://localhost:4200";
        var inviteLink = $"{frontendUrl}/cadastro/convite?token={token}";
        var subject = $"Você foi convidado para se juntar à {editora.Nome}";

        await _emailService.SendEmailAsync(
            email,
            subject,
            BuildInviteEmailBody(inviter.Usuario?.Nome ?? inviter.Id.ToString(), editora.Nome, inviteLink, ExpiracaoConviteHoras));

        // 8. Audit log (best-effort: must never block the invite operation).
        await AddAuditLogAsync("ConviteEnviado", tenantId, inviterId, email, convite.Id);

        return new InviteUserResult(convite.Id);
    }

    public async Task<InviteDetailsDto> GetInviteDetailsAsync(string token)
    {
        var convite = await _context.Convites
            .IgnoreQueryFilters()
            .Include(c => c.Editora)
            .FirstOrDefaultAsync(c => c.Token == token)
            ?? throw new InvalidOperationException("Convite não encontrado ou inválido.");

        if (convite.Status != ConviteStatus.Pendente)
            throw new InvalidOperationException("Este convite já foi processado.");

        if (convite.DataExpiracao < DateTime.UtcNow)
            throw new InvalidOperationException("Este convite expirou.");

        // Check if user already exists in the global identity table
        var email = new DomoLibri.Domain.ValueObjects.Email(convite.Email);
        var usuarioExiste = await _context.Usuarios
            .AnyAsync(u => u.Email == email);

        return new InviteDetailsDto(
            convite.Email,
            convite.Editora?.Nome ?? "Editora",
            convite.RoleId,
            usuarioExiste);
    }

    public async Task AcceptInviteAsync(AcceptInviteDto dto)
    {
        // 1. Fetch the invitation
        var convite = await _context.Convites
            .IgnoreQueryFilters()
            .Include(c => c.Editora)
            .FirstOrDefaultAsync(c => c.Token == dto.Token)
            ?? throw new InvalidOperationException("Convite não encontrado ou inválido.");

        if (convite.Status != ConviteStatus.Pendente)
            throw new InvalidOperationException("Este convite já foi processado.");

        if (convite.DataExpiracao < DateTime.UtcNow)
        {
            convite.Status = ConviteStatus.Expirado;
            await _context.SaveChangesAsync();
            throw new InvalidOperationException("Este convite expirou.");
        }

        var email = new DomoLibri.Domain.ValueObjects.Email(convite.Email);

        await using var transaction = await _context.Database.BeginTransactionAsync();

        // 2. Resolve the global user identity
        var usuario = await _context.Usuarios
            .FirstOrDefaultAsync(u => u.Email == email);

        if (usuario is null)
        {
            // New user identity creation
            if (string.IsNullOrWhiteSpace(dto.Nome) || string.IsNullOrWhiteSpace(dto.Senha))
                throw new InvalidOperationException("Nome e Senha são obrigatórios para novos usuários.");

            var senhaHash = BCrypt.Net.BCrypt.HashPassword(dto.Senha);
            usuario = new Usuario(convite.Email, senhaHash, dto.Nome);
            usuario.ConfirmarEmail(); // Email is considered verified because they clicked the link
            _context.Usuarios.Add(usuario);
        }

        // 3. Create the per-tenant binding (vinculo)
        //    Ensure no duplicate binding (global Query Filter ignored for safety check)
        var jaVinculado = await _context.VinculosUsuarioEditora
            .IgnoreQueryFilters()
            .AnyAsync(v => v.UsuarioId == usuario.Id && v.EditoraId == convite.EditoraId);

        if (jaVinculado)
        {
            convite.Status = ConviteStatus.Aceito;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return; // Already linked, just complete the invite
        }

        var role = await _context.Roles
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == convite.RoleId)
            ?? throw new InvalidOperationException("A role do convite não foi encontrada.");

        var vinculo = new VinculoUsuarioEditora
        {
            Id = Guid.NewGuid(),
            EditoraId = convite.EditoraId,
            UsuarioId = usuario.Id,
            Ativo = true,
            DataEntrada = DateTime.UtcNow,
            TipoVinculo = TipoVinculo.Colaborador, // Default mapping
            Roles = [role]
        };

        _context.VinculosUsuarioEditora.Add(vinculo);

        // 4. Update invite status
        convite.Status = ConviteStatus.Aceito;
        convite.DataResposta = DateTime.UtcNow;

        // 5. Audit log
        await AddAuditLogAsync("ConviteAceito", convite.EditoraId, usuario.Id, convite.Email, convite.Id);

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static string GenerateInviteToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // 128-bit entropy

    private static string BuildInviteEmailBody(
        string inviterNome, string editoraNome, string inviteLink, int expiresInHours)
    {
        var html = @"<!DOCTYPE html>
<html lang=""pt-BR"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""ie=edge"">
    <title>Convite DomoLibri</title>
    <style type=""text/css"">
        body, table, td, div, p, a { -webkit-text-size-adjust: 100%; -ms-text-size-adjust: 100%; }
        table, td { mso-table-lspace: 0pt; mso-table-rspace: 0pt; }
        img { border: 0; outline: none; text-decoration: none; -ms-interpolation-mode: nearest-neighbor; }
        body { margin: 0; padding: 0; min-width: 100% !important; }
        .body-wrap { width: 100%; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; }
        .header-table { width: 100%; background-color: #0078D4; }
        .header-text { color: #ffffff; font-family: Arial, sans-serif; font-size: 32px; font-weight: bold; text-align: center; padding: 30px 20px; margin: 0; }
        .content-table { width: 100%; background-color: #ffffff; }
        .content-cell { padding: 40px; font-family: Arial, 'Helvetica Neue', Helvetica, sans-serif; font-size: 15px; color: #333; line-height: 1.6; }
        .greeting { font-size: 16px; margin-bottom: 20px; margin-top: 0; }
        .message { margin-bottom: 15px; }
        .message strong { color: #0078D4; }
        .button-cell { text-align: center; padding: 30px 0; }
        .button-link { display: inline-block; background-color: #0078D4; color: #ffffff; padding: 14px 40px; text-decoration: none; font-weight: bold; font-size: 16px; border-radius: 4px; font-family: Arial, sans-serif; }
        .button-link:hover { background-color: #106ebe; }
        .expiration-box { background-color: #f9f3e6; border-left: 4px solid #f59e0b; padding: 15px; margin: 25px 0; font-size: 13px; color: #92400e; font-family: Arial, sans-serif; }
        .fallback-link { font-size: 12px; color: #999; word-break: break-all; margin-top: 20px; padding-top: 20px; border-top: 1px solid #eee; font-family: Arial, sans-serif; }
        .footer-table { width: 100%; background-color: #f9f9f9; border-top: 1px solid #e8e8e8; }
        .footer-cell { padding: 25px; font-family: Arial, sans-serif; font-size: 12px; color: #666; line-height: 1.6; text-align: center; }
        .footer-disclaimer { background-color: #f0f4f8; border: 1px solid #d1dce6; padding: 12px; margin-bottom: 15px; font-size: 11px; color: #556b7f; border-radius: 3px; }
        .footer-links { margin-top: 15px; }
        .footer-links a { color: #0078D4; text-decoration: none; margin: 0 8px; font-size: 12px; }
    </style>
</head>
<body>
    <table class=""body-wrap"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr>
            <td align=""center"" valign=""top"">
                <table class=""container"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                    <!-- Header -->
                    <tr>
                        <td class=""header-table"">
                            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                                <tr>
                                    <td align=""center"" valign=""middle"">
                                        <h1 class=""header-text"">DomoLibri</h1>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Content -->
                    <tr>
                        <td class=""content-table"">
                            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                                <tr>
                                    <td class=""content-cell"">
                                        <p class=""greeting"">Olá!</p>
                                        
                                        <p class=""message"">
                                            <strong>" + inviterNome + @"</strong> convidou você para se juntar à editora <strong>" + editoraNome + @"</strong> no <strong>DomoLibri</strong>.
                                        </p>
                                        
                                        <p class=""message"">
                                            Clique no botão abaixo para aceitar o convite e ativar seu acesso:
                                        </p>

                                        <!-- CTA Button -->
                                        <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                                            <tr>
                                                <td class=""button-cell"">
                                                    <a href=""" + inviteLink + @""" class=""button-link"">Aceitar Convite</a>
                                                </td>
                                            </tr>
                                        </table>

                                        <!-- Expiration Notice -->
                                        <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                                            <tr>
                                                <td class=""expiration-box"">
                                                    <strong>⏱️ Válido por " + expiresInHours + @" horas</strong><br>
                                                    Este link expirará em " + expiresInHours + @" horas. Certifique-se de aceitar o convite em tempo hábil.
                                                </td>
                                            </tr>
                                        </table>

                                        <!-- Fallback Link -->
                                        <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                                            <tr>
                                                <td class=""fallback-link"">
                                                    <strong>Ou copie e cole este link no seu navegador:</strong><br>
                                                    " + inviteLink + @"
                                                </td>
                                            </tr>
                                        </table>

                                        <p class=""message"" style=""margin-top: 20px;"">
                                            Se você não esperava este e-mail, pode ignorá-lo com segurança.
                                        </p>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>

                    <!-- Footer -->
                    <tr>
                        <td class=""footer-table"">
                            <table width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                                <tr>
                                    <td class=""footer-cell"">
                                        <div class=""footer-disclaimer"">
                                            🔒 <strong>Segurança:</strong> Nunca compartilhe este link ou as suas credenciais. A DomoLibri nunca pedirá suas informações sensíveis por e-mail.
                                        </div>
                                        
                                        <p style=""margin: 0 0 15px 0; color: #999; font-size: 12px;"">
                                            © 2024 DomoLibri. Todos os direitos reservados.
                                        </p>

                                        <div class=""footer-links"">
                                            <a href=""https://domolibri.com/privacidade"">Política de Privacidade</a> •
                                            <a href=""https://domolibri.com/termos"">Termos de Uso</a> •
                                            <a href=""https://domolibri.com/contato"">Suporte</a>
                                        </div>
                                    </td>
                                </tr>
                            </table>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";

        return html;
    }

    private async Task AddAuditLogAsync(
        string acao, Guid tenantId, Guid usuarioId, string emailConvidado, Guid conviteId)
    {
        try
        {
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                EditoraId = tenantId,
                UsuarioId = usuarioId,
                Acao = acao,
                Recurso = "ConviteUsuario",
                RecursoId = conviteId.ToString(),
                IP = _userContextProvider.GetIp() ?? "",
                UserAgent = _userContextProvider.GetUserAgent() ?? "",
                DataHora = DateTime.UtcNow,
                DadosNovos = $"{{\"email\":\"{emailConvidado}\"}}"
            });
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Best-effort: audit log failures must never block the invite operation.
        }
    }
}
