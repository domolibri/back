using DomoLibri.Application.DTOs;
using DomoLibri.Application.Interfaces;
using DomoLibri.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DomoLibri.Api.Controllers.Onboarding;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EditoraController : ControllerBase
{
    private readonly IStorageService _storageService;
    private readonly ITenantProvider _tenantProvider;
    private readonly DomoLibriDbContext _db;

    public EditoraController(
        IStorageService storageService,
        ITenantProvider tenantProvider,
        DomoLibriDbContext db)
    {
        _storageService = storageService;
        _tenantProvider = tenantProvider;
        _db = db;
    }

    /// <summary>
    /// Updates branding settings (logo and/or primary color) for the authenticated Editora.
    /// </summary>
    [HttpPatch("branding")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UpdateBranding([FromForm] UpdateBrandingRequest request)
    {
        var tenantId = _tenantProvider.GetTenantId();
        if (tenantId is null)
            return Unauthorized();

        var editora = await _db.Editoras.FindAsync(tenantId.Value);
        if (editora is null)
            return NotFound(new { Message = "Editora não encontrada." });

        if (request.Logo is not null)
            editora.LogoUrl = await _storageService.UploadLogoAsync(request.Logo, tenantId.Value);

        if (request.CorPrimaria is not null)
            editora.CorPrimaria = request.CorPrimaria;

        await _db.SaveChangesAsync();

        return Ok(new
        {
            editora.LogoUrl,
            editora.CorPrimaria
        });
    }
}
