using GraphScopeProxy.Core.Models;
using Microsoft.Graph.Models;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Service for Microsoft Graph API operations
/// </summary>
public interface IGraphApiService
{
    /// <summary>
    /// Gets all members of a group with paging support
    /// </summary>
    /// <param name="groupId">The group ID to get members for</param>
    /// <returns>List of group members</returns>
    Task<List<DirectoryObject>> GetGroupMembersAsync(string groupId);

    /// <summary>
    /// Gets group information
    /// </summary>
    /// <param name="groupId">The group ID</param>
    /// <returns>Group information</returns>
    Task<Group?> GetGroupAsync(string groupId);

    /// <summary>
    /// Gets user information by ID
    /// </summary>
    /// <param name="userId">The user ID</param>
    /// <returns>User information</returns>
    Task<User?> GetUserAsync(string userId);

    /// <summary>
    /// Gets places (rooms/workspaces) from Graph Places API
    /// </summary>
    /// <returns>List of places</returns>
    Task<List<Place>> GetPlacesAsync();

    /// <summary>
    /// Checks if the Graph API connection is healthy
    /// </summary>
    /// <returns>True if connection is working</returns>
    Task<bool> IsHealthyAsync();
}
