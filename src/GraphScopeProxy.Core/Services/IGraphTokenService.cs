namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Interface for managing Microsoft Graph access tokens
/// </summary>
public interface IGraphTokenService
{
    /// <summary>
    /// Get a valid access token for Microsoft Graph API
    /// </summary>
    /// <returns>Access token string</returns>
    Task<string> GetAccessTokenAsync();

    /// <summary>
    /// Check if the current token is valid and not expired
    /// </summary>
    /// <returns>True if token is valid</returns>
    Task<bool> IsTokenValidAsync();

    /// <summary>
    /// Force refresh of the access token
    /// </summary>
    /// <returns>New access token</returns>
    Task<string> RefreshTokenAsync();
}
