using Microsoft.AspNetCore.Http;

namespace DomoLibri.Application.DTOs;

public record UpdateBrandingRequest(
    string? CorPrimaria,
    IFormFile? Logo
);
