using GraphScopeProxy.Core.Models;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Interface for caching resource scopes
/// </summary>
public interface IScopeCache
{
    /// <summary>
    /// Store a resource scope in cache
    /// </summary>
    /// <param name="tokenId">Unique token identifier</param>
    /// <param name="scope">Resource scope to cache</param>
    /// <param name="expiration">Cache expiration time</param>
    Task SetAsync(string tokenId, ResourceScope scope, TimeSpan expiration);

    /// <summary>
    /// Retrieve a resource scope from cache
    /// </summary>
    /// <param name="tokenId">Unique token identifier</param>
    /// <returns>Resource scope if found, null otherwise</returns>
    Task<ResourceScope?> GetAsync(string tokenId);

    /// <summary>
    /// Remove a resource scope from cache
    /// </summary>
    /// <param name="tokenId">Unique token identifier</param>
    Task RemoveAsync(string tokenId);

    /// <summary>
    /// Remove all cached scopes for a specific group
    /// </summary>
    /// <param name="groupId">Group identifier</param>
    Task RemoveByGroupAsync(string groupId);
}
