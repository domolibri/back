using System.ComponentModel.DataAnnotations;
using DomoLibri.Api;
using DomoLibri.Application.Services;
using DomoLibri.Domain.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DomoLibri.Api.Controllers;

public record InviteUserRequest(
    [Required(ErrorMessage = "E-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "Informe um e-mail válido.")]
    string Email,

    [Required(ErrorMessage = "Role é obrigatória.")]
    Guid RoleId);

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IInvitationService _invitationService;
    private readonly ITenantProvider _tenantProvider;
    private readonly DomoLibriDbContext _db;

    public UsersController(
        IInvitationService invitationService,
        ITenantProvider tenantProvider,
        DomoLibriDbContext db)
    {
        _invitationService = invitationService;
        _tenantProvider = tenantProvider;
        _db = db;
    }

    /// <summary>
    /// Returns all roles defined for the current tenant's Editora.
    /// </summary>
    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
    {
        var roles = await _db.Roles
            .Select(r => new { r.Id, r.Nome, r.Descricao })
            .ToListAsync();

        return Ok(roles);
    }

    /// <summary>
    /// Sends an invite e-mail to a new colleague, associating them with a specific role.
    /// </summary>
    [HttpPost("invite")]
    public async Task<IActionResult> InviteUser([FromBody] InviteUserRequest request)
    {
        try
        {
            var result = await _invitationService.InviteUserAsync(
                new InviteUserDto(request.Email, request.RoleId));

            return Ok(new { result.ConviteId, Message = "Convite enviado com sucesso." });
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
    /// Retrieves invitation details. Public endpoint (no auth required for the token owner).
    /// </summary>
    [HttpGet("invite/{token}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetInviteDetails(string token)
    {
        try
        {
            var result = await _invitationService.GetInviteDetailsAsync(token);
            return Ok(result);
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
    /// Completes the invitation. Public endpoint (no auth required).
    /// </summary>
    [HttpPost("invite/accept")]
    [AllowAnonymous]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteDto request)
    {
        try
        {
            await _invitationService.AcceptInviteAsync(request);
            return Ok(new { Message = "Convite aceito com sucesso. Agora você pode fazer login." });
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
    /// Updates roles for a VinculoUsuarioEditora. Tenant isolation enforced.
    /// </summary>
    [HttpPut("{id}/roles")]
    public async Task<IActionResult> UpdateVinculoRoles(Guid id, [FromBody] Guid[] roleIds)
    {
        try
        {
            var tenantId = _tenantProvider.GetTenantId();

            var vinculo = await _db.VinculosUsuarioEditora
                .Include(v => v.Roles)
                .FirstOrDefaultAsync(v => v.Id == id && v.EditoraId == tenantId);

            if (vinculo == null)
                return NotFound(new { Message = "Vínculo não encontrado." });

            // Verify all roles exist and belong to current tenant
            var roles = await _db.Roles
                .Where(r => roleIds.Contains(r.Id) && r.EditoraId == tenantId)
                .ToListAsync();

            if (roles.Count != roleIds.Length)
                return BadRequest(new { Message = "Uma ou mais roles não existem ou não pertencem à editora atual." });

            // Update roles collection
            vinculo.Roles.Clear();
            foreach (var role in roles)
            {
                vinculo.Roles.Add(role);
            }

            await _db.SaveChangesAsync();
            return NoContent();
        }
        catch (Exception ex)
        {
            return Problem(
                detail: ex.Message,
                instance: HttpContext.Request.Path,
                statusCode: StatusCodes.Status500InternalServerError,
                title: ProblemDetailsHelper.GetTitle(500),
                type: ProblemDetailsHelper.GetTypeUri(500));
        }
    }
}
