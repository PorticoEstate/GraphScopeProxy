using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using GraphScopeProxy.Core.Services;
using GraphScopeProxy.Core.Models;

namespace GraphScopeProxy.Api.Controllers;

/// <summary>
/// Auth controller for login and token management
/// </summary>
[ApiController]
[Route("auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly ILogger<AuthController> _logger;
    private readonly IApiKeyService _apiKeyService;
    private readonly IJwtService _jwtService;
    private readonly IResourceClassifier _resourceClassifier;
    private readonly IScopeCache _scopeCache;

    public AuthController(
        ILogger<AuthController> logger,
        IApiKeyService apiKeyService,
        IJwtService jwtService,
        IResourceClassifier resourceClassifier,
        IScopeCache scopeCache)
    {
        _logger = logger;
        _apiKeyService = apiKeyService;
        _jwtService = jwtService;
        _resourceClassifier = resourceClassifier;
        _scopeCache = scopeCache;
    }

    /// <summary>
    /// Login endpoint to establish resource scope and get JWT token
    /// </summary>
    /// <param name="request">Login request with API key and group ID</param>
    /// <returns>JWT token and scope information</returns>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Login attempt for group {GroupId}", request.GroupId);
        
        try
        {
            // Validate API key
            if (string.IsNullOrWhiteSpace(request.ApiKey))
            {
                _logger.LogWarning("Login attempt with empty API key for group {GroupId}", request.GroupId);
                return BadRequest(new { error = "API key is required" });
            }

            // Check if API key has access to the specified group
            var hasAccess = await _apiKeyService.HasAccessToGroupAsync(request.ApiKey, request.GroupId);
            if (!hasAccess)
            {
                _logger.LogWarning("Login attempt with invalid API key {ApiKey} for group {GroupId}", 
                    request.ApiKey[..Math.Min(8, request.ApiKey.Length)] + "...", request.GroupId);
                return Unauthorized(new { error = "Invalid API key or insufficient permissions for the specified group" });
            }

            // Get allowed resources for the group
            _logger.LogInformation("Fetching allowed resources for group {GroupId}", request.GroupId);
            var allowedResources = await _resourceClassifier.GetAllowedResourcesAsync(request.GroupId);
            
            if (!allowedResources.Any())
            {
                _logger.LogWarning("No resources found for group {GroupId}", request.GroupId);
                return NotFound(new { error = "No accessible resources found for the specified group" });
            }

            // Generate JWT token
            var token = _jwtService.GenerateToken(request.ApiKey, request.GroupId, allowedResources);

            // Cache the resource scope for middleware use
            var resourceScope = new ResourceScope
            {
                GroupId = request.GroupId,
                AllowedResources = allowedResources.ToList(),
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(15) // Should match JWT expiration
            };

            var cacheKey = $"scope:{request.GroupId}:{request.ApiKey}";
            await _scopeCache.SetAsync(cacheKey, resourceScope, TimeSpan.FromMinutes(15));

            var response = new LoginResponse
            {
                Token = token,
                GroupId = request.GroupId,
                ResourceCount = allowedResources.Count(),
                ExpiresIn = 900 // 15 minutes in seconds
            };

            _logger.LogInformation("Successful login for group {GroupId} with {ResourceCount} accessible resources", 
                request.GroupId, allowedResources.Count());

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for group {GroupId}", request.GroupId);
            return StatusCode(500, new { error = "An error occurred during login" });
        }
    }

    /// <summary>
    /// Refresh an existing token
    /// </summary>
    /// <returns>New JWT token</returns>
    [HttpPost("refresh")]
    [Authorize]
    public async Task<IActionResult> Refresh()
    {
        _logger.LogInformation("Token refresh requested");
        
        try
        {
            // Get current token from Authorization header
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return BadRequest(new { error = "Invalid authorization header" });
            }

            var currentToken = authHeader.Substring("Bearer ".Length);
            
            // Validate current token and extract scope
            var resourceScope = _jwtService.ValidateToken(currentToken);
            if (resourceScope == null)
            {
                return Unauthorized(new { error = "Invalid or expired token" });
            }

            // Generate new token with same scope
            var newToken = _jwtService.GenerateToken("refreshed", resourceScope.GroupId, resourceScope.AllowedResources);
            
            // Invalidate old token
            await _jwtService.InvalidateTokenAsync(currentToken);

            var response = new LoginResponse
            {
                Token = newToken,
                GroupId = resourceScope.GroupId,
                ResourceCount = resourceScope.AllowedResources.Count,
                ExpiresIn = 900
            };

            _logger.LogInformation("Token refreshed successfully for group {GroupId}", resourceScope.GroupId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token refresh");
            return StatusCode(500, new { error = "An error occurred during token refresh" });
        }
    }

    /// <summary>
    /// Logout and invalidate token
    /// </summary>
    /// <returns>Success response</returns>
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        _logger.LogInformation("Logout requested");
        
        try
        {
            // Get current token from Authorization header
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            {
                return BadRequest(new { error = "Invalid authorization header" });
            }

            var currentToken = authHeader.Substring("Bearer ".Length);
            
            // Invalidate the token
            var success = await _jwtService.InvalidateTokenAsync(currentToken);
            
            if (success)
            {
                _logger.LogInformation("User logged out successfully");
                return Ok(new { message = "Logged out successfully" });
            }
            else
            {
                _logger.LogWarning("Failed to invalidate token during logout");
                return BadRequest(new { error = "Failed to invalidate token" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            return StatusCode(500, new { error = "An error occurred during logout" });
        }
    }
}
