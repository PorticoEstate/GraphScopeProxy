namespace GraphScopeProxy.Core.Models;

/// <summary>
/// Types of resources that can be classified
/// </summary>
public enum ResourceType
{
    /// <summary>
    /// Meeting room resource
    /// </summary>
    Room,

    /// <summary>
    /// Workspace or desk resource
    /// </summary>
    Workspace,

    /// <summary>
    /// Equipment resource (projectors, devices, etc.)
    /// </summary>
    Equipment,

    /// <summary>
    /// Generic resource type
    /// </summary>
    Generic
}
