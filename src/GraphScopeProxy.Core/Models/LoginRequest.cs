namespace GraphScopeProxy.Core.Models;

/// <summary>
/// Represents a login request with API key and group information
/// </summary>
public class LoginRequest
{
    /// <summary>
    /// API key for authentication
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD Group ID to establish resource scope
    /// </summary>
    public string GroupId { get; set; } = string.Empty;

    /// <summary>
    /// Optional group alias for easier identification
    /// </summary>
    public string? GroupAlias { get; set; }
}
