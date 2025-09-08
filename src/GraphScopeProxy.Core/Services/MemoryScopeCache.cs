using GraphScopeProxy.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// In-memory implementation of scope cache
/// </summary>
public class MemoryScopeCache : IScopeCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<MemoryScopeCache> _logger;
    private readonly ConcurrentDictionary<string, HashSet<string>> _groupToTokens = new();

    public MemoryScopeCache(IMemoryCache cache, ILogger<MemoryScopeCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task SetAsync(string tokenId, ResourceScope scope, TimeSpan expiration)
    {
        try
        {
            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                Priority = CacheItemPriority.Normal
            };

            cacheEntryOptions.RegisterPostEvictionCallback(OnEvicted);

            _cache.Set(GetCacheKey(tokenId), scope, cacheEntryOptions);

            // Track tokens by group for bulk removal
            _groupToTokens.AddOrUpdate(
                scope.GroupId,
                new HashSet<string> { tokenId },
                (key, existing) =>
                {
                    existing.Add(tokenId);
                    return existing;
                });

            _logger.LogDebug("Cached scope for token {TokenId} with {ResourceCount} resources", 
                tokenId, scope.AllowedResources.Count);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache scope for token {TokenId}", tokenId);
            throw;
        }
    }

    public Task<ResourceScope?> GetAsync(string tokenId)
    {
        try
        {
            var scope = _cache.Get<ResourceScope>(GetCacheKey(tokenId));
            
            if (scope != null)
            {
                _logger.LogDebug("Retrieved scope for token {TokenId} with {ResourceCount} resources", 
                    tokenId, scope.AllowedResources.Count);
            }
            else
            {
                _logger.LogDebug("Scope not found for token {TokenId}", tokenId);
            }

            return Task.FromResult(scope);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve scope for token {TokenId}", tokenId);
            return Task.FromResult<ResourceScope?>(null);
        }
    }

    public Task RemoveAsync(string tokenId)
    {
        try
        {
            _cache.Remove(GetCacheKey(tokenId));
            
            // Remove from group tracking
            RemoveTokenFromGroupTracking(tokenId);

            _logger.LogDebug("Removed scope for token {TokenId}", tokenId);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove scope for token {TokenId}", tokenId);
            throw;
        }
    }

    public Task RemoveByGroupAsync(string groupId)
    {
        try
        {
            if (_groupToTokens.TryGetValue(groupId, out var tokens))
            {
                foreach (var tokenId in tokens.ToList())
                {
                    _cache.Remove(GetCacheKey(tokenId));
                }

                _groupToTokens.TryRemove(groupId, out _);
                
                _logger.LogInformation("Removed {Count} cached scopes for group {GroupId}", 
                    tokens.Count, groupId);
            }

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove scopes for group {GroupId}", groupId);
            throw;
        }
    }

    private static string GetCacheKey(string tokenId) => $"scope:{tokenId}";

    private void OnEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (value is ResourceScope scope && key is string cacheKey)
        {
            var tokenId = cacheKey.Replace("scope:", "");
            RemoveTokenFromGroupTracking(tokenId);
            
            _logger.LogDebug("Scope evicted for token {TokenId}, reason: {Reason}", 
                tokenId, reason);
        }
    }

    private void RemoveTokenFromGroupTracking(string tokenId)
    {
        foreach (var kvp in _groupToTokens)
        {
            if (kvp.Value.Remove(tokenId) && kvp.Value.Count == 0)
            {
                _groupToTokens.TryRemove(kvp.Key, out _);
            }
        }
    }
}
