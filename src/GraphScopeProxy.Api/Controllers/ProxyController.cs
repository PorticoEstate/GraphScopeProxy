using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GraphScopeProxy.Core.Models;
using GraphScopeProxy.Core.Services;
using System.Text;
using System.Text.Json;
using System.Net;

namespace GraphScopeProxy.Api.Controllers;

/// <summary>
/// Proxy controller that forwards all Graph API calls with resource scope enforcement
/// </summary>
[ApiController]
[Authorize]
[Route("v1.0")]
public class ProxyController : ControllerBase
{
    private readonly IGraphProxyService _graphProxyService;
    private readonly ILogger<ProxyController> _logger;

    public ProxyController(
        IGraphProxyService graphProxyService,
        ILogger<ProxyController> logger)
    {
        _graphProxyService = graphProxyService;
        _logger = logger;
    }

    /// <summary>
    /// Proxy all HTTP methods to Microsoft Graph API with scope enforcement
    /// </summary>
    [HttpGet("{*path}")]
    [HttpPost("{*path}")]
    [HttpPut("{*path}")]
    [HttpPatch("{*path}")]
    [HttpDelete("{*path}")]
    public async Task<IActionResult> ProxyToGraph(string? path = "")
    {
        try
        {
            // Get resource scope from context (set by ResourceScopeMiddleware)
            if (!HttpContext.Items.TryGetValue("ResourceScope", out var scopeObj) || 
                scopeObj is not ResourceScope scope)
            {
                _logger.LogWarning("No resource scope found in request context for path: {Path}", path);
                return Unauthorized("Invalid or expired token");
            }

            var correlationId = HttpContext.TraceIdentifier;
            _logger.LogInformation("Proxying {Method} request to {Path} for scope {TokenId}", 
                Request.Method, path, scope.TokenId);

            // Check if this request requires resource-level authorization
            var resourceValidationResult = await ValidateResourceAccess(path ?? "", scope);
            if (!resourceValidationResult.IsAllowed)
            {
                _logger.LogWarning("Access denied to resource {ResourceId} for scope {TokenId}", 
                    resourceValidationResult.ResourceId, scope.TokenId);
                return Forbid($"Access denied to resource: {resourceValidationResult.ResourceId}");
            }

            // Forward the request to Microsoft Graph
            var requestHeaders = Request.Headers.ToDictionary(
                h => h.Key, 
                h => h.Value.AsEnumerable().Cast<string>());

            var graphResponse = await _graphProxyService.ForwardRequestAsync(
                Request.Method,
                path ?? "",
                Request.Query.ToString() ?? "",
                requestHeaders,
                Request.Body,
                correlationId);

            // Check if response needs filtering
            if (RequiresResponseFiltering(path ?? ""))
            {
                var filteredResponse = await FilterResponseContent(graphResponse, scope);
                return Content(filteredResponse.Content, filteredResponse.ContentType);
            }

            // Return unmodified response
            Response.StatusCode = (int)graphResponse.StatusCode;
            
            // Copy response headers (excluding content-related ones that ASP.NET Core handles)
            foreach (var header in graphResponse.Headers)
            {
                if (!ShouldSkipHeader(header.Key))
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            return Content(graphResponse.Content, graphResponse.ContentType);
        }
        catch (HttpRequestException ex) when (ex.Data.Contains("StatusCode"))
        {
            var statusCode = (HttpStatusCode)ex.Data["StatusCode"]!;
            _logger.LogWarning("Graph API returned {StatusCode} for path {Path}: {Message}", 
                statusCode, path, ex.Message);
            return StatusCode((int)statusCode, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying request to path: {Path}", path);
            return StatusCode(500, "Internal server error during proxy operation");
        }
    }

    /// <summary>
    /// Validate if the user has access to the specific resource mentioned in the request
    /// </summary>
    private Task<(bool IsAllowed, string? ResourceId)> ValidateResourceAccess(string path, ResourceScope scope)
    {
        // Extract resource identifiers from the path
        var resourceId = ExtractResourceId(path);
        
        if (string.IsNullOrEmpty(resourceId))
        {
            // No specific resource targeted, allow request
            return Task.FromResult((true, (string?)null));
        }

        // Check if the resource is in the allowed scope
        var isAllowed = scope.AllowedResources.Any(r => 
            string.Equals(r.Id, resourceId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Mail, resourceId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult((isAllowed, (string?)resourceId));
    }

    /// <summary>
    /// Extract resource ID from URL path for access validation
    /// </summary>
    private string? ExtractResourceId(string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var pathParts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        // Pattern: /users/{id}/calendar or /users/{id}/calendars
        // Pattern: /users/{id}/events
        // Pattern: /calendars/{id}
        for (int i = 0; i < pathParts.Length - 1; i++)
        {
            var part = pathParts[i].ToLowerInvariant();
            
            if (part == "users" && i + 1 < pathParts.Length)
            {
                var userId = pathParts[i + 1];
                
                // Check if this is a calendar-related request
                if (i + 2 < pathParts.Length)
                {
                    var nextPart = pathParts[i + 2].ToLowerInvariant();
                    if (nextPart == "calendar" || nextPart == "calendars" || nextPart == "events")
                    {
                        return userId; // The user/resource ID
                    }
                }
                return userId;
            }
            
            if (part == "calendars" && i + 1 < pathParts.Length)
            {
                return pathParts[i + 1]; // Calendar ID
            }
        }

        return null;
    }

    /// <summary>
    /// Check if the response from this path should be filtered
    /// </summary>
    private bool RequiresResponseFiltering(string path)
    {
        var normalizedPath = path.ToLowerInvariant();
        
        // List endpoints that return collections that need filtering
        return normalizedPath.Contains("/rooms") || 
               normalizedPath.Contains("/places") ||
               normalizedPath.EndsWith("/calendars") ||
               (normalizedPath.Contains("/users") && normalizedPath.Contains("/calendars"));
    }

    /// <summary>
    /// Filter response content to only include allowed resources
    /// </summary>
    private Task<(string Content, string ContentType)> FilterResponseContent(
        (HttpStatusCode StatusCode, string Content, string ContentType, Dictionary<string, IEnumerable<string>> Headers) graphResponse, 
        ResourceScope scope)
    {
        try
        {
            if (string.IsNullOrEmpty(graphResponse.Content))
            {
                return Task.FromResult((graphResponse.Content, graphResponse.ContentType));
            }

            using var jsonDoc = JsonDocument.Parse(graphResponse.Content);
            
            // Handle paginated responses with "value" array
            if (jsonDoc.RootElement.TryGetProperty("value", out var valueElement) && 
                valueElement.ValueKind == JsonValueKind.Array)
            {
                var filteredItems = new List<JsonElement>();
                
                foreach (var item in valueElement.EnumerateArray())
                {
                    if (IsItemAllowed(item, scope))
                    {
                        filteredItems.Add(item);
                    }
                }

                // Rebuild the response with filtered items
                var filteredResponse = RebuildGraphResponse(jsonDoc.RootElement, filteredItems);
                return Task.FromResult((filteredResponse, graphResponse.ContentType));
            }
            
            // Handle single item responses
            if (IsItemAllowed(jsonDoc.RootElement, scope))
            {
                return Task.FromResult((graphResponse.Content, graphResponse.ContentType));
            }
            
            // Item not allowed, return empty response
            return Task.FromResult(("{}", graphResponse.ContentType));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Graph response as JSON, returning unfiltered");
            return Task.FromResult((graphResponse.Content, graphResponse.ContentType));
        }
    }

    /// <summary>
    /// Check if a JSON item is allowed based on resource scope
    /// </summary>
    private bool IsItemAllowed(JsonElement item, ResourceScope scope)
    {
        // Try to get identifier from various possible properties
        var identifiers = new List<string?>();
        
        if (item.TryGetProperty("id", out var idElement))
            identifiers.Add(idElement.GetString());
            
        if (item.TryGetProperty("emailAddress", out var emailElement))
        {
            if (emailElement.TryGetProperty("address", out var addressElement))
                identifiers.Add(addressElement.GetString());
        }
        
        if (item.TryGetProperty("mail", out var mailElement))
            identifiers.Add(mailElement.GetString());
            
        if (item.TryGetProperty("userPrincipalName", out var upnElement))
            identifiers.Add(upnElement.GetString());

        // Check if any identifier matches our allowed resources
        return identifiers.Any(id => 
            !string.IsNullOrEmpty(id) && 
            scope.AllowedResources.Any(r => 
                string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(r.Mail, id, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Rebuild Graph API response with filtered items while preserving structure
    /// </summary>
    private string RebuildGraphResponse(JsonElement originalRoot, List<JsonElement> filteredItems)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        
        writer.WriteStartObject();
        
        // Copy all original properties except "value"
        foreach (var property in originalRoot.EnumerateObject())
        {
            if (property.Name != "value")
            {
                property.Value.WriteTo(writer);
            }
        }
        
        // Write filtered "value" array
        writer.WritePropertyName("value");
        writer.WriteStartArray();
        
        foreach (var item in filteredItems)
        {
            item.WriteTo(writer);
        }
        
        writer.WriteEndArray();
        writer.WriteEndObject();
        
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Check if response header should be skipped when copying
    /// </summary>
    private bool ShouldSkipHeader(string headerName)
    {
        var lowerHeaderName = headerName.ToLowerInvariant();
        
        // Skip headers that ASP.NET Core handles automatically
        return lowerHeaderName == "content-length" ||
               lowerHeaderName == "content-type" ||
               lowerHeaderName == "transfer-encoding" ||
               lowerHeaderName == "connection";
    }
}
