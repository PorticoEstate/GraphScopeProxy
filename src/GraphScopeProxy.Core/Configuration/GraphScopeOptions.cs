namespace GraphScopeProxy.Core.Configuration;

/// <summary>
/// Configuration options for GraphScope Proxy
/// </summary>
public class GraphScopeOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "GraphScope";

    /// <summary>
    /// Azure AD Tenant ID
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD Application (Client) ID
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Azure AD Application Client Secret
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// JWT signing key for token generation
    /// </summary>
    public string JwtSigningKey { get; set; } = string.Empty;

    /// <summary>
    /// JWT secret for token signing (alternative to JwtSigningKey)
    /// </summary>
    public string JwtSecret { get; set; } = string.Empty;

    /// <summary>
    /// JWT issuer
    /// </summary>
    public string JwtIssuer { get; set; } = "GraphScopeProxy";

    /// <summary>
    /// JWT audience
    /// </summary>
    public string JwtAudience { get; set; } = "GraphScopeProxy";

    /// <summary>
    /// List of allowed place types (room, workspace, equipment)
    /// </summary>
    public List<string> AllowedPlaceTypes { get; set; } = new() { "room", "workspace" };

    /// <summary>
    /// Whether to allow generic resources that don't match specific types
    /// </summary>
    public bool AllowGenericResources { get; set; } = false;

    /// <summary>
    /// Cache TTL in seconds for resource scopes
    /// </summary>
    public int ScopeCacheTtlSeconds { get; set; } = 900; // 15 minutes

    /// <summary>
    /// Maximum number of resources allowed in a scope
    /// </summary>
    public int MaxScopeSize { get; set; } = 500;

    /// <summary>
    /// Whether group alias is required in login requests
    /// </summary>
    public bool RequireGroupAlias { get; set; } = false;

    /// <summary>
    /// List of valid API keys for authentication
    /// </summary>
    public List<string> ApiKeys { get; set; } = new();

    /// <summary>
    /// Redis connection string (optional, falls back to in-memory cache)
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// JWT token expiration in seconds
    /// </summary>
    public int JwtExpirationSeconds { get; set; } = 900; // 15 minutes

    /// <summary>
    /// Whether to use Graph Places API to supplement group member data
    /// </summary>
    public bool UseGraphPlacesApi { get; set; } = true;

    /// <summary>
    /// Maximum number of Graph API calls per minute (rate limiting)
    /// </summary>
    public int MaxGraphCallsPerMinute { get; set; } = 100;

    /// <summary>
    /// Whether to enable detailed request/response logging
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;
}
