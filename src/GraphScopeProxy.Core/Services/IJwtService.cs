using GraphScopeProxy.Core.Models;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Service for JWT token generation and validation
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Generates a JWT token for the specified group ID and resources
    /// </summary>
    /// <param name="apiKey">The API key that was validated</param>
    /// <param name="groupId">The group ID for resource scoping</param>
    /// <param name="allowedResources">The resources this token grants access to</param>
    /// <returns>JWT token string</returns>
    string GenerateToken(string apiKey, string groupId, IEnumerable<AllowedResource> allowedResources);

    /// <summary>
    /// Validates a JWT token and extracts the resource scope
    /// </summary>
    /// <param name="token">The JWT token to validate</param>
    /// <returns>Resource scope if valid, null if invalid</returns>
    ResourceScope? ValidateToken(string token);

    /// <summary>
    /// Invalidates a JWT token (adds to blacklist)
    /// </summary>
    /// <param name="token">The token to invalidate</param>
    /// <returns>True if successfully invalidated</returns>
    Task<bool> InvalidateTokenAsync(string token);

    /// <summary>
    /// Checks if a token is blacklisted
    /// </summary>
    /// <param name="jti">The JWT ID (jti claim)</param>
    /// <returns>True if blacklisted</returns>
    Task<bool> IsTokenBlacklistedAsync(string jti);
}
