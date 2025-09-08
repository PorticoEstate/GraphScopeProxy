using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Service for proxying HTTP requests to Microsoft Graph API
/// </summary>
public class GraphProxyService : IGraphProxyService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGraphTokenService _graphTokenService;
    private readonly ILogger<GraphProxyService> _logger;
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0/";

    public GraphProxyService(
        IHttpClientFactory httpClientFactory,
        IGraphTokenService graphTokenService,
        ILogger<GraphProxyService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _graphTokenService = graphTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Forward an HTTP request to Microsoft Graph API
    /// </summary>
    public async Task<(HttpStatusCode StatusCode, string Content, string ContentType, Dictionary<string, IEnumerable<string>> Headers)> 
        ForwardRequestAsync(
            string method, 
            string path, 
            string queryString, 
            IDictionary<string, IEnumerable<string>> requestHeaders, 
            Stream requestBody, 
            string correlationId)
    {
        using var httpClient = _httpClientFactory.CreateClient("GraphProxy");
        
        try
        {
            // Get access token for Graph API
            var accessToken = await _graphTokenService.GetAccessTokenAsync();
            
            // Build the target URL
            var targetUrl = BuildTargetUrl(path, queryString);
            _logger.LogDebug("Forwarding {Method} request to {Url} with correlation ID {CorrelationId}", 
                method, targetUrl, correlationId);

            // Create the HTTP request
            using var request = new HttpRequestMessage(new HttpMethod(method), targetUrl);
            
            // Set authorization header
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            
            // Copy relevant headers from original request
            CopyRequestHeaders(requestHeaders, request, correlationId);
            
            // Copy request body if present
            if (requestBody != null && requestBody.Length > 0)
            {
                requestBody.Seek(0, SeekOrigin.Begin);
                var content = new StreamContent(requestBody);
                
                // Set content type if specified
                if (requestHeaders.TryGetValue("Content-Type", out var contentType))
                {
                    content.Headers.TryAddWithoutValidation("Content-Type", contentType.FirstOrDefault());
                }
                
                request.Content = content;
            }

            // Send the request
            using var response = await httpClient.SendAsync(request);
            
            // Read response content
            var responseContent = await response.Content.ReadAsStringAsync();
            var responseContentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            
            // Extract response headers
            var responseHeaders = new Dictionary<string, IEnumerable<string>>();
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                responseHeaders[header.Key] = header.Value;
            }
            
            _logger.LogInformation("Graph API responded with {StatusCode} for {Method} {Path} (correlation: {CorrelationId})", 
                response.StatusCode, method, path, correlationId);
            
            return (response.StatusCode, responseContent, responseContentType, responseHeaders);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error while forwarding request to Graph API (correlation: {CorrelationId})", correlationId);
            
            // Try to extract status code from exception
            var statusCode = HttpStatusCode.BadGateway;
            if (ex.Data.Contains("StatusCode"))
            {
                statusCode = (HttpStatusCode)ex.Data["StatusCode"]!;
            }
            
            var errorContent = $"{{\"error\": {{\"code\": \"ProxyError\", \"message\": \"{ex.Message}\"}}}}";
            return (statusCode, errorContent, "application/json", new Dictionary<string, IEnumerable<string>>());
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning("Request to Graph API timed out (correlation: {CorrelationId})", correlationId);
            var timeoutContent = "{\"error\": {\"code\": \"RequestTimeout\", \"message\": \"Request to Microsoft Graph timed out\"}}";
            return (HttpStatusCode.RequestTimeout, timeoutContent, "application/json", new Dictionary<string, IEnumerable<string>>());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while forwarding request to Graph API (correlation: {CorrelationId})", correlationId);
            var errorContent = "{\"error\": {\"code\": \"InternalError\", \"message\": \"Internal proxy error\"}}";
            return (HttpStatusCode.InternalServerError, errorContent, "application/json", new Dictionary<string, IEnumerable<string>>());
        }
    }

    /// <summary>
    /// Build the target URL for Microsoft Graph API
    /// </summary>
    private string BuildTargetUrl(string path, string queryString)
    {
        // Determine base URL based on path
        string baseUrl;
        if (path.StartsWith("beta/", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "https://graph.microsoft.com/";
            // Path already includes "beta/" prefix
        }
        else
        {
            baseUrl = GraphBaseUrl;
            // Remove leading slash if present for v1.0 paths
            if (path.StartsWith('/'))
                path = path[1..];
        }
        
        var url = baseUrl;
        
        if (!string.IsNullOrEmpty(path))
        {
            url += path;
        }
        
        if (!string.IsNullOrEmpty(queryString))
        {
            // Remove leading ? if present
            if (queryString.StartsWith('?'))
                queryString = queryString[1..];
                
            url += "?" + queryString;
        }
        
        return url;
    }

    /// <summary>
    /// Copy relevant headers from the original request to the Graph API request
    /// </summary>
    private void CopyRequestHeaders(IDictionary<string, IEnumerable<string>> requestHeaders, HttpRequestMessage request, string correlationId)
    {
        foreach (var header in requestHeaders)
        {
            var headerName = header.Key.ToLowerInvariant();
            
            // Skip headers that should not be forwarded
            if (ShouldSkipRequestHeader(headerName))
                continue;
            
            try
            {
                // Add header to appropriate collection
                if (IsContentHeader(headerName))
                {
                    // Content headers will be set with content
                    continue;
                }
                else
                {
                    // Request headers
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to copy header {HeaderName} (correlation: {CorrelationId})", header.Key, correlationId);
            }
        }
        
        // Always add our correlation ID
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);
        
        // Ensure we have a proper User-Agent
        if (!request.Headers.Contains("User-Agent"))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", "GraphScopeProxy/1.0");
        }
    }

    /// <summary>
    /// Check if a request header should be skipped when forwarding
    /// </summary>
    private bool ShouldSkipRequestHeader(string headerName)
    {
        // Headers that should not be forwarded to Graph API
        var skipHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "host",
            "authorization",      // We set our own Bearer token
            "content-length",     // Will be calculated automatically
            "transfer-encoding",
            "connection",
            "upgrade",
            "proxy-authenticate",
            "proxy-authorization",
            "te",
            "trailer"
        };
        
        return skipHeaders.Contains(headerName);
    }

    /// <summary>
    /// Check if a header is a content header (vs request header)
    /// </summary>
    private bool IsContentHeader(string headerName)
    {
        var contentHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "content-type",
            "content-length",
            "content-encoding",
            "content-language",
            "content-location",
            "content-md5",
            "content-range",
            "expires",
            "last-modified"
        };
        
        return contentHeaders.Contains(headerName);
    }
}
