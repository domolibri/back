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
}
