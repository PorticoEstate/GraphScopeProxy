using GraphScopeProxy.Core.Models;
using GraphScopeProxy.Core.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Redis-based implementation of scope cache
/// </summary>
public class RedisScopeCache : IScopeCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisScopeCache> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisScopeCache(IDistributedCache cache, ILogger<RedisScopeCache> logger)
    {
        _cache = cache;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task SetAsync(string tokenId, ResourceScope scope, TimeSpan expiration)
    {
        try
        {
            var json = JsonSerializer.Serialize(scope, _jsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration
            };

            await _cache.SetStringAsync(GetCacheKey(tokenId), json, options);

            // Also track by group for bulk removal
            await TrackTokenForGroupAsync(scope.GroupId, tokenId, expiration);

            _logger.LogDebug("Cached scope for token {TokenId} with {ResourceCount} resources in Redis", 
                tokenId, scope.AllowedResources.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cache scope for token {TokenId} in Redis", tokenId);
            throw;
        }
    }

    public async Task<ResourceScope?> GetAsync(string tokenId)
    {
        try
        {
            var json = await _cache.GetStringAsync(GetCacheKey(tokenId));
            
            if (string.IsNullOrEmpty(json))
            {
                _logger.LogDebug("Scope not found for token {TokenId} in Redis", tokenId);
                return null;
            }

            var scope = JsonSerializer.Deserialize<ResourceScope>(json, _jsonOptions);
            
            if (scope != null)
            {
                _logger.LogDebug("Retrieved scope for token {TokenId} with {ResourceCount} resources from Redis", 
                    tokenId, scope.AllowedResources.Count);
            }

            return scope;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve scope for token {TokenId} from Redis", tokenId);
            return null;
        }
    }

    public async Task RemoveAsync(string tokenId)
    {
        try
        {
            await _cache.RemoveAsync(GetCacheKey(tokenId));
            
            _logger.LogDebug("Removed scope for token {TokenId} from Redis", tokenId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove scope for token {TokenId} from Redis", tokenId);
            throw;
        }
    }

    public async Task RemoveByGroupAsync(string groupId)
    {
        try
        {
            var groupKey = GetGroupKey(groupId);
            var tokensJson = await _cache.GetStringAsync(groupKey);
            
            if (!string.IsNullOrEmpty(tokensJson))
            {
                var tokens = JsonSerializer.Deserialize<HashSet<string>>(tokensJson, _jsonOptions);
                
                if (tokens != null)
                {
                    var tasks = tokens.Select(tokenId => _cache.RemoveAsync(GetCacheKey(tokenId)));
                    await Task.WhenAll(tasks);
                    
                    await _cache.RemoveAsync(groupKey);
                    
                    _logger.LogInformation("Removed {Count} cached scopes for group {GroupId} from Redis", 
                        tokens.Count, groupId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove scopes for group {GroupId} from Redis", groupId);
            throw;
        }
    }

    private async Task TrackTokenForGroupAsync(string groupId, string tokenId, TimeSpan expiration)
    {
        try
        {
            var groupKey = GetGroupKey(groupId);
            var existingJson = await _cache.GetStringAsync(groupKey);
            
            var tokens = string.IsNullOrEmpty(existingJson) 
                ? new HashSet<string>() 
                : JsonSerializer.Deserialize<HashSet<string>>(existingJson, _jsonOptions) ?? new HashSet<string>();
            
            tokens.Add(tokenId);
            
            var json = JsonSerializer.Serialize(tokens, _jsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration.Add(TimeSpan.FromMinutes(5)) // Keep group tracking a bit longer
            };
            
            await _cache.SetStringAsync(groupKey, json, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track token {TokenId} for group {GroupId}", tokenId, groupId);
            // Don't throw - this is not critical
        }
    }

    private static string GetCacheKey(string tokenId) => $"scope:{tokenId}";
    private static string GetGroupKey(string groupId) => $"group:{groupId}:tokens";
}
