using Microsoft.AspNetCore.Http;

namespace DomoLibri.Application.Interfaces;

public interface IStorageService
{
    Task<string> UploadLogoAsync(IFormFile logoFile, Guid tenantId);
}
