namespace GraphScopeProxy.Core.Models;

/// <summary>
/// Represents a resource that is allowed within a scope
/// </summary>
public class AllowedResource
{
    /// <summary>
    /// Unique identifier of the resource
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Email address or UPN of the resource (normalized to lowercase)
    /// </summary>
    public string Mail { get; set; } = string.Empty;

    /// <summary>
    /// Type of the resource
    /// </summary>
    public ResourceType Type { get; set; }

    /// <summary>
    /// Display name of the resource
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Capacity of the resource (for rooms/workspaces)
    /// </summary>
    public int? Capacity { get; set; }

    /// <summary>
    /// Location or building of the resource
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Additional properties for future extensibility
    /// </summary>
    public Dictionary<string, object>? AdditionalProperties { get; set; }
}
