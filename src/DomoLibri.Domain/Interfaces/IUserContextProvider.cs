namespace DomoLibri.Domain.Interfaces;

/// <summary>
/// Provides information about the current user and HTTP request context.
/// Returns nulls when called outside an HTTP request (e.g., background jobs).
/// </summary>
public interface IUserContextProvider
{
    /// <summary>The authenticated user's ID, or null if unauthenticated.</summary>
    Guid? GetUserId();

    /// <summary>The client's IP address, or null if not in an HTTP context.</summary>
    string? GetIp();

    /// <summary>The client's User-Agent header, or null if not in an HTTP context.</summary>
    string? GetUserAgent();
}
