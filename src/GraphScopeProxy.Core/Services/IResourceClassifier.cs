using GraphScopeProxy.Core.Models;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Interface for classifying and retrieving allowed resources from Azure AD groups
/// </summary>
public interface IResourceClassifier
{
    /// <summary>
    /// Get all allowed resources for a specific group
    /// </summary>
    /// <param name="groupId">Azure AD Group ID</param>
    /// <returns>List of allowed resources</returns>
    Task<List<AllowedResource>> GetAllowedResourcesAsync(string groupId);

    /// <summary>
    /// Classify a single resource based on its properties
    /// </summary>
    /// <param name="email">Resource email address</param>
    /// <param name="displayName">Resource display name</param>
    /// <param name="description">Resource description</param>
    /// <returns>Tuple with resource type, capacity, and location</returns>
    (ResourceType Type, int? Capacity, string? Location) ClassifyResource(string email, string? displayName = null, string? description = null);

    /// <summary>
    /// Validate if a resource type is allowed based on configuration
    /// </summary>
    /// <param name="resourceType">Resource type to validate</param>
    /// <returns>True if resource type is allowed</returns>
    bool IsResourceTypeAllowed(ResourceType resourceType);
}
