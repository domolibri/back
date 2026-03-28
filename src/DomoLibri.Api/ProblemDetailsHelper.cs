namespace DomoLibri.Api;

/// <summary>
/// Maps HTTP status codes to RFC 9457 problem type URIs (referencing RFC 9110)
/// and stable title strings, as required by RFC 9457 §3.
/// </summary>
internal static class ProblemDetailsHelper
{
    public static string GetTypeUri(int statusCode) => statusCode switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        401 => "https://tools.ietf.org/html/rfc9110#section-15.5.2",
        403 => "https://tools.ietf.org/html/rfc9110#section-15.5.4",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        422 => "https://tools.ietf.org/html/rfc9110#section-15.5.21",
        500 => "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        _   => "about:blank"
    };

    /// <summary>
    /// Returns the stable, type-level title as defined by RFC 9457 §3.1.
    /// Must not vary between occurrences of the same problem type.
    /// </summary>
    public static string GetTitle(int statusCode) => statusCode switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        422 => "Unprocessable Content",
        500 => "Internal Server Error",
        _   => "Error"
    };
}
