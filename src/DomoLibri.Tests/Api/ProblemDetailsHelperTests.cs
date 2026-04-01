using DomoLibri.Api;

namespace DomoLibri.Tests.Api;

public class ProblemDetailsHelperTests
{
    #region GetTypeUri

    [Theory]
    [InlineData(400, "https://tools.ietf.org/html/rfc9110#section-15.5.1")]
    [InlineData(401, "https://tools.ietf.org/html/rfc9110#section-15.5.2")]
    [InlineData(403, "https://tools.ietf.org/html/rfc9110#section-15.5.4")]
    [InlineData(404, "https://tools.ietf.org/html/rfc9110#section-15.5.5")]
    [InlineData(409, "https://tools.ietf.org/html/rfc9110#section-15.5.10")]
    [InlineData(422, "https://tools.ietf.org/html/rfc9110#section-15.5.21")]
    [InlineData(500, "https://tools.ietf.org/html/rfc9110#section-15.6.1")]
    public void GetTypeUri_KnownStatusCode_ReturnsRfcUri(int statusCode, string expectedUri)
    {
        var result = ProblemDetailsHelper.GetTypeUri(statusCode);
        Assert.Equal(expectedUri, result);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(0)]
    public void GetTypeUri_UnknownStatusCode_ReturnsAboutBlank(int statusCode)
    {
        var result = ProblemDetailsHelper.GetTypeUri(statusCode);
        Assert.Equal("about:blank", result);
    }

    #endregion

    #region GetTitle

    [Theory]
    [InlineData(400, "Bad Request")]
    [InlineData(401, "Unauthorized")]
    [InlineData(403, "Forbidden")]
    [InlineData(404, "Not Found")]
    [InlineData(409, "Conflict")]
    [InlineData(422, "Unprocessable Content")]
    [InlineData(500, "Internal Server Error")]
    public void GetTitle_KnownStatusCode_ReturnsStableTitle(int statusCode, string expectedTitle)
    {
        var result = ProblemDetailsHelper.GetTitle(statusCode);
        Assert.Equal(expectedTitle, result);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(0)]
    public void GetTitle_UnknownStatusCode_ReturnsError(int statusCode)
    {
        var result = ProblemDetailsHelper.GetTitle(statusCode);
        Assert.Equal("Error", result);
    }

    #endregion
}
