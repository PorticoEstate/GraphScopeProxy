namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Service for API key validation and management
/// </summary>
public interface IApiKeyService
{
    /// <summary>
    /// Validates an API key and returns the associated group ID if valid
    /// </summary>
    /// <param name="apiKey">The API key to validate</param>
    /// <returns>Group ID if valid, null if invalid</returns>
    Task<string?> ValidateApiKeyAsync(string apiKey);

    /// <summary>
    /// Gets all group IDs associated with an API key
    /// </summary>
    /// <param name="apiKey">The API key</param>
    /// <returns>List of group IDs</returns>
    Task<IEnumerable<string>> GetGroupsForApiKeyAsync(string apiKey);

    /// <summary>
    /// Checks if an API key has access to a specific group
    /// </summary>
    /// <param name="apiKey">The API key</param>
    /// <param name="groupId">The group ID to check</param>
    /// <returns>True if API key has access to the group</returns>
    Task<bool> HasAccessToGroupAsync(string apiKey, string groupId);
}
