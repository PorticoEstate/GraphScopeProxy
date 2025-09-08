using Microsoft.Extensions.Options;
using GraphScopeProxy.Core.Configuration;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Simple API key service implementation
/// In production, this should connect to a database or external service
/// </summary>
public class ApiKeyService : IApiKeyService
{
    private readonly GraphScopeOptions _options;
    private readonly IScopeCache _scopeCache;

    // For demo purposes, we'll use a simple dictionary
    // In production, this should be stored in a database
    private readonly Dictionary<string, List<string>> _apiKeyToGroups = new()
    {
        // Example API keys and their associated groups
        { "test-api-key-1", new List<string> { "12345678-1234-1234-1234-123456789012" } },
        { "test-api-key-2", new List<string> { "87654321-4321-4321-4321-210987654321" } },
        { "admin-api-key", new List<string> { 
            "12345678-1234-1234-1234-123456789012", 
            "87654321-4321-4321-4321-210987654321" 
        } }
    };

    public ApiKeyService(IOptions<GraphScopeOptions> options, IScopeCache scopeCache)
    {
        _options = options.Value;
        _scopeCache = scopeCache;
    }

    public async Task<string?> ValidateApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        // Skip cache for API key validation since IScopeCache is for ResourceScope objects
        // In production, this would be a database lookup that could be cached separately

        // Validate API key (in production, this would be a database lookup)
        var groups = await GetGroupsForApiKeyAsync(apiKey);
        var firstGroup = groups.FirstOrDefault();

        return firstGroup;
    }

    public async Task<IEnumerable<string>> GetGroupsForApiKeyAsync(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return Enumerable.Empty<string>();

        await Task.Delay(1); // Simulate async operation

        // In production, this would be a database query
        return _apiKeyToGroups.GetValueOrDefault(apiKey, new List<string>());
    }

    public async Task<bool> HasAccessToGroupAsync(string apiKey, string groupId)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(groupId))
            return false;

        var groups = await GetGroupsForApiKeyAsync(apiKey);
        return groups.Contains(groupId);
    }
}
