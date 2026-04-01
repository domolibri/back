using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;

namespace DomoLibri.Tests.Api;

public class HttpTenantProviderTests
{
    [Fact]
    public void GetTenantId_NullHttpContext_ReturnsNull()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var sut = new HttpTenantProvider(accessor.Object);

        Assert.Null(sut.GetTenantId());
    }

    [Fact]
    public void GetTenantId_ValidJwtClaim_ReturnsTenantId()
    {
        var tenantId = Guid.NewGuid();
        var claims = new[] { new Claim("tenant_id", tenantId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var sut = new HttpTenantProvider(accessor.Object);

        Assert.Equal(tenantId, sut.GetTenantId());
    }

    [Fact]
    public void GetTenantId_InvalidGuidInJwtClaim_ReturnsNullForAuthenticatedUser()
    {
        // Authenticated user with invalid tenant_id claim → no header fallback
        var claims = new[] { new Claim("tenant_id", "not-a-guid") };
        var identity = new ClaimsIdentity(claims, "Test"); // Test = authenticated
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var sut = new HttpTenantProvider(accessor.Object);

        Assert.Null(sut.GetTenantId());
    }

    [Fact]
    public void GetTenantId_UnauthenticatedWithValidHeader_ReturnsTenantId()
    {
        var tenantId = Guid.NewGuid();

        var httpContext = new DefaultHttpContext();
        // No identity set → IsAuthenticated = false
        httpContext.Request.Headers["X-Tenant-Id"] = tenantId.ToString();

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var sut = new HttpTenantProvider(accessor.Object);

        Assert.Equal(tenantId, sut.GetTenantId());
    }

    [Fact]
    public void GetTenantId_UnauthenticatedWithInvalidHeader_ReturnsNull()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Tenant-Id"] = "not-a-valid-guid";

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var sut = new HttpTenantProvider(accessor.Object);

        Assert.Null(sut.GetTenantId());
    }

    [Fact]
    public void GetTenantId_UnauthenticatedWithNoHeader_ReturnsNull()
    {
        var httpContext = new DefaultHttpContext();

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var sut = new HttpTenantProvider(accessor.Object);

        Assert.Null(sut.GetTenantId());
    }

    [Fact]
    public void GetTenantId_AuthenticatedWithNoTenantClaim_ReturnsNull()
    {
        // Authenticated but no tenant_id claim → header must be ignored
        var claims = new[] { new Claim(ClaimTypes.Email, "user@test.com") };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Request.Headers["X-Tenant-Id"] = Guid.NewGuid().ToString();

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var sut = new HttpTenantProvider(accessor.Object);

        Assert.Null(sut.GetTenantId());
    }

    [Fact]
    public void GetTenantId_AuthenticatedWithValidClaim_IgnoresHeader()
    {
        var tenantIdFromClaim = Guid.NewGuid();
        var tenantIdFromHeader = Guid.NewGuid();

        var claims = new[] { new Claim("tenant_id", tenantIdFromClaim.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext { User = principal };
        httpContext.Request.Headers["X-Tenant-Id"] = tenantIdFromHeader.ToString();

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var sut = new HttpTenantProvider(accessor.Object);

        // Claim takes priority over header
        Assert.Equal(tenantIdFromClaim, sut.GetTenantId());
    }
}
