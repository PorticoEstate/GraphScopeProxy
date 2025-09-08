namespace GraphScopeProxy.Core.Models;

/// <summary>
/// Represents a successful login response with JWT token and scope information
/// </summary>
public class LoginResponse
{
    /// <summary>
    /// JWT token for subsequent requests
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// Group ID that was used to establish the scope
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Number of resources available in the scope
    /// </summary>
    public int ResourceCount { get; set; }

    /// <summary>
    /// Token expiration time in seconds
    /// </summary>
    public int ExpiresIn { get; set; }
}
