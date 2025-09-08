namespace GraphScopeProxy.Core.Models;

/// <summary>
/// Represents a cached resource scope for a specific token
/// </summary>
public class ResourceScope
{
    /// <summary>
    /// Unique token identifier
    /// </summary>
    public string TokenId { get; set; } = string.Empty;

    /// <summary>
    /// Group ID that established this scope
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// List of resources allowed within this scope
    /// </summary>
    public List<AllowedResource> AllowedResources { get; set; } = new();

    /// <summary>
    /// When this scope was created
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When this scope will expire
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Optional metadata about the scope
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
