using GraphScopeProxy.Core.Services;

namespace GraphScopeProxy.Api.Middleware;

/// <summary>
/// Middleware for enforcing resource scope access control
/// </summary>
public class ResourceScopeMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IScopeCache _scopeCache;
    private readonly ILogger<ResourceScopeMiddleware> _logger;

    public ResourceScopeMiddleware(
        RequestDelegate next,
        IScopeCache scopeCache,
        ILogger<ResourceScopeMiddleware> logger)
    {
        _next = next;
        _scopeCache = scopeCache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip middleware for auth and admin endpoints
        if (ShouldSkipMiddleware(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Check if user is authenticated
        if (!context.User.Identity?.IsAuthenticated ?? true)
        {
            _logger.LogWarning("Unauthenticated request to protected endpoint: {Path}", 
                context.Request.Path);
            
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Authentication required");
            return;
        }

        // Extract token ID from JWT claims
        var tokenIdClaim = context.User.FindFirst("tid");
        if (tokenIdClaim == null)
        {
            _logger.LogWarning("No token ID found in JWT claims for path: {Path}", 
                context.Request.Path);
            
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid token");
            return;
        }

        // Get resource scope from cache
        var scope = await _scopeCache.GetAsync(tokenIdClaim.Value);
        if (scope == null)
        {
            _logger.LogWarning("Resource scope not found for token {TokenId}", tokenIdClaim.Value);
            
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Token expired or invalid");
            return;
        }

        // Add scope to context for use in controllers
        context.Items["ResourceScope"] = scope;

        _logger.LogDebug("Resource scope validated for token {TokenId} with {ResourceCount} resources", 
            tokenIdClaim.Value, scope.AllowedResources.Count);

        await _next(context);
    }

    private static bool ShouldSkipMiddleware(PathString path)
    {
        var pathValue = path.Value?.ToLowerInvariant() ?? "";
        
        return pathValue.StartsWith("/auth") ||
               pathValue.StartsWith("/admin") ||
               pathValue.StartsWith("/health") ||
               pathValue.StartsWith("/swagger") ||
               pathValue == "/" ||
               pathValue == "/favicon.ico";
    }
}
