using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GraphScopeProxy.Core.Models;
using GraphScopeProxy.Core.Services;

namespace GraphScopeProxy.Api.Controllers;

/// <summary>
/// Proxy controller for Microsoft Graph beta endpoints
/// </summary>
[ApiController]
[Authorize]
[Route("beta")]
public class BetaProxyController : ControllerBase
{
    private readonly IGraphProxyService _graphProxyService;
    private readonly ILogger<BetaProxyController> _logger;

    public BetaProxyController(
        IGraphProxyService graphProxyService,
        ILogger<BetaProxyController> logger)
    {
        _graphProxyService = graphProxyService;
        _logger = logger;
    }

    /// <summary>
    /// Proxy all HTTP methods to Microsoft Graph beta API with scope enforcement
    /// </summary>
    [HttpGet("{*path}")]
    [HttpPost("{*path}")]
    [HttpPut("{*path}")]
    [HttpPatch("{*path}")]
    [HttpDelete("{*path}")]
    public async Task<IActionResult> ProxyToBetaGraph(string? path = "")
    {
        try
        {
            // Get resource scope from context (set by ResourceScopeMiddleware)
            if (!HttpContext.Items.TryGetValue("ResourceScope", out var scopeObj) || 
                scopeObj is not ResourceScope scope)
            {
                _logger.LogWarning("No resource scope found in request context for beta path: {Path}", path);
                return Unauthorized("Invalid or expired token");
            }

            var correlationId = HttpContext.TraceIdentifier;
            _logger.LogInformation("Proxying {Method} request to beta/{Path} for scope {TokenId}", 
                Request.Method, path, scope.TokenId);

            // Note: Beta endpoint uses different base URL, so we need a modified proxy service
            // For now, we'll proxy through the same service but with beta path prefix
            var betaPath = $"beta/{path}";

            // Forward the request to Microsoft Graph beta
            var requestHeaders = Request.Headers.ToDictionary(
                h => h.Key, 
                h => h.Value.AsEnumerable().Cast<string>());

            var graphResponse = await _graphProxyService.ForwardRequestAsync(
                Request.Method,
                betaPath,
                Request.Query.ToString() ?? "",
                requestHeaders,
                Request.Body,
                correlationId);

            Response.StatusCode = (int)graphResponse.StatusCode;
            
            // Copy response headers
            foreach (var header in graphResponse.Headers)
            {
                if (!ShouldSkipHeader(header.Key))
                {
                    Response.Headers[header.Key] = header.Value.ToArray();
                }
            }

            return Content(graphResponse.Content, graphResponse.ContentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error proxying beta request to path: {Path}", path);
            return StatusCode(500, "Internal server error during proxy operation");
        }
    }

    private bool ShouldSkipHeader(string headerName)
    {
        var lowerHeaderName = headerName.ToLowerInvariant();
        return lowerHeaderName == "content-length" ||
               lowerHeaderName == "content-type" ||
               lowerHeaderName == "transfer-encoding" ||
               lowerHeaderName == "connection";
    }
}
