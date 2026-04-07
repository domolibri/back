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
            .AnyAsync(v => v.Usuario!.Email == email);

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
        var inviteLink = $"{frontendUrl}/register?token={token}";
        var subject = $"Você foi convidado para se juntar à {editora.Nome}";

        await _emailService.SendEmailAsync(
            email,
            subject,
            BuildInviteEmailBody(inviter.Usuario?.Nome ?? inviter.Id.ToString(), editora.Nome, inviteLink, ExpiracaoConviteHoras));

        // 8. Audit log (best-effort: must never block the invite operation).
        await AddAuditLogAsync("ConviteEnviado", tenantId, inviterId, email, convite.Id);

        return new InviteUserResult(convite.Id);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private static string GenerateInviteToken()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)); // 128-bit entropy

    private static string BuildInviteEmailBody(
        string inviterNome, string editoraNome, string inviteLink, int expiresInHours)
    {
        return $"""
            <p>Olá!</p>
            <p><strong>{inviterNome}</strong> convidou você para se juntar à editora <strong>{editoraNome}</strong> no DomoLibri.</p>
            <p>Clique no link abaixo para aceitar o convite e criar a sua conta. O link é válido por {expiresInHours} horas.</p>
            <p><a href="{inviteLink}">{inviteLink}</a></p>
            <p>Se você não esperava este convite, ignore este e-mail.</p>
            """;
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
