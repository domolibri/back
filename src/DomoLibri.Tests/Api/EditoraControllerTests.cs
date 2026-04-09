using DomoLibri.Application.DTOs;
using DomoLibri.Application.Interfaces;
using DomoLibri.Domain.Entities;
using DomoLibri.Infrastructure.Data;
using DomoLibri.Api.Controllers.Onboarding;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DomoLibri.Tests.Api;

public class EditoraControllerTests
{
    private static (EditoraController controller, Mock<IStorageService> storageMock, DomoLibriDbContext db)
        CreateController(Guid? tenantId = null)
    {
        var storageMock = new Mock<IStorageService>();
        var tenantProviderMock = new Mock<ITenantProvider>();
        tenantProviderMock.Setup(t => t.GetTenantId()).Returns(tenantId);

        var opts = new DbContextOptionsBuilder<DomoLibriDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new DomoLibriDbContext(opts, tenantProviderMock.Object);

        var controller = new EditoraController(storageMock.Object, tenantProviderMock.Object, db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return (controller, storageMock, db);
    }

    [Fact]
    public async Task GetBranding_NoTenantId_Returns401Unauthorized()
    {
        var (controller, _, _) = CreateController(null);

        var result = await controller.GetBranding();

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task GetBranding_EditoraNotFound_Returns404NotFound()
    {
        var (controller, _, _) = CreateController(Guid.NewGuid());

        var result = await controller.GetBranding();

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task GetBranding_WithExistingBranding_ReturnsBrandingData()
    {
        var editora = new Editora("E", "e");
        editora.AtualizarBranding("https://storage.example.com/logo.png", "#FF5500");
        var (controller, _, db) = CreateController(editora.Id);

        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var result = await controller.GetBranding();

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);

        var data = ok.Value!;
        var type = data.GetType();
        Assert.Equal("https://storage.example.com/logo.png", type.GetProperty("LogoUrl")!.GetValue(data));
        Assert.Equal("#FF5500", type.GetProperty("CorPrimaria")!.GetValue(data));
    }

    [Fact]
    public async Task GetBranding_WithNoBrandingConfigured_ReturnsNullFields()
    {
        var editora = new Editora("E", "e");
        var (controller, _, db) = CreateController(editora.Id);

        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var result = await controller.GetBranding();

        var ok = Assert.IsType<OkObjectResult>(result);
        var data = ok.Value!;
        var type = data.GetType();
        Assert.Null(type.GetProperty("LogoUrl")!.GetValue(data));
        Assert.Null(type.GetProperty("CorPrimaria")!.GetValue(data));
    }

    [Fact]
    public async Task UpdateBranding_NoTenantId_Returns401Unauthorized()
    {
        var (controller, _, _) = CreateController(null);

        var result = await controller.UpdateBranding(new UpdateBrandingRequest(null, null));

        Assert.IsType<UnauthorizedResult>(result);
    }

    [Fact]
    public async Task UpdateBranding_EditoraNotFound_Returns404NotFound()
    {
        var (controller, _, _) = CreateController(Guid.NewGuid());

        var result = await controller.UpdateBranding(new UpdateBrandingRequest(null, null));

        var notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task UpdateBranding_WithCorPrimaria_UpdatesColorAndReturns200()
    {
        var editora = new Editora("E", "e");
        var (controller, _, db) = CreateController(editora.Id);

        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var result = await controller.UpdateBranding(new UpdateBrandingRequest("#FF0000", null));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);

        var updated = await db.Editoras.FindAsync(editora.Id);
        Assert.Equal("#FF0000", updated!.CorPrimaria);
    }

    [Fact]
    public async Task UpdateBranding_WithLogo_UploadsAndReturnsUrl()
    {
        var editora = new Editora("E", "e");
        var (controller, storageMock, db) = CreateController(editora.Id);

        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var logoMock = new Mock<IFormFile>();
        logoMock.Setup(f => f.FileName).Returns("logo.png");
        logoMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1, 2, 3 }));

        storageMock.Setup(s => s.UploadLogoAsync(It.IsAny<IFormFile>(), editora.Id))
            .ReturnsAsync("https://storage.example.com/logo.png");

        var result = await controller.UpdateBranding(new UpdateBrandingRequest(null, logoMock.Object));

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);

        var updated = await db.Editoras.FindAsync(editora.Id);
        Assert.Equal("https://storage.example.com/logo.png", updated!.LogoUrl);
        storageMock.Verify(s => s.UploadLogoAsync(It.IsAny<IFormFile>(), editora.Id), Times.Once);
    }

    [Fact]
    public async Task UpdateBranding_WithBothLogoAndColor_UpdatesBoth()
    {
        var editora = new Editora("E", "e");
        var (controller, storageMock, db) = CreateController(editora.Id);

        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var logoMock = new Mock<IFormFile>();
        logoMock.Setup(f => f.FileName).Returns("logo.png");
        logoMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1 }));

        storageMock.Setup(s => s.UploadLogoAsync(It.IsAny<IFormFile>(), editora.Id))
            .ReturnsAsync("https://storage.example.com/newlogo.png");

        var result = await controller.UpdateBranding(new UpdateBrandingRequest("#0000FF", logoMock.Object));

        Assert.IsType<OkObjectResult>(result);

        var updated = await db.Editoras.FindAsync(editora.Id);
        Assert.Equal("https://storage.example.com/newlogo.png", updated!.LogoUrl);
        Assert.Equal("#0000FF", updated.CorPrimaria);
    }

    [Fact]
    public async Task UpdateBranding_NullLogoAndColor_DoesNotChangeExistingValues()
    {
        var editora = new Editora("E", "e");
        editora.AtualizarBranding("https://existing.com/logo.png", "#123456");
        var (controller, storageMock, db) = CreateController(editora.Id);

        db.Editoras.Add(editora);
        await db.SaveChangesAsync();

        var result = await controller.UpdateBranding(new UpdateBrandingRequest(null, null));

        Assert.IsType<OkObjectResult>(result);

        var updated = await db.Editoras.FindAsync(editora.Id);
        Assert.Equal("https://existing.com/logo.png", updated!.LogoUrl);
        Assert.Equal("#123456", updated.CorPrimaria);
        storageMock.Verify(s => s.UploadLogoAsync(It.IsAny<IFormFile>(), It.IsAny<Guid>()), Times.Never);
    }
}
