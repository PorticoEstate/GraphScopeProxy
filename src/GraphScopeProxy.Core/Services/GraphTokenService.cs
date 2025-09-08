using Microsoft.Graph;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Service for managing Microsoft Graph access tokens
/// </summary>
public class GraphTokenService : IGraphTokenService
{
    private readonly GraphServiceClient _graphClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphTokenService> _logger;
    private const string TokenCacheKey = "graph_access_token";

    public GraphTokenService(
        GraphServiceClient graphClient,
        IMemoryCache cache,
        ILogger<GraphTokenService> logger)
    {
        _graphClient = graphClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        if (_cache.TryGetValue(TokenCacheKey, out string? cachedToken) && !string.IsNullOrEmpty(cachedToken))
        {
            _logger.LogDebug("Using cached Graph access token");
            return cachedToken;
        }

        return await RefreshTokenAsync();
    }

    public async Task<bool> IsTokenValidAsync()
    {
        try
        {
            // Simple validation - try to make a basic Graph call
            await _graphClient.Me.GetAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return false;
        }
    }

    public async Task<string> RefreshTokenAsync()
    {
        try
        {
            _logger.LogDebug("Refreshing Graph access token");

            // The GraphServiceClient handles token acquisition automatically
            // We'll make a simple call to ensure authentication
            await _graphClient.Me.GetAsync();

            // For this implementation, we'll return a placeholder
            // In a real implementation, you would extract the actual token
            var token = "graph_sdk_managed_token";

            // Cache for 55 minutes (tokens typically expire after 60 minutes)
            _cache.Set(TokenCacheKey, token, TimeSpan.FromMinutes(55));

            _logger.LogInformation("Graph access token refreshed successfully");
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh Graph access token");
            throw;
        }
    }
}
