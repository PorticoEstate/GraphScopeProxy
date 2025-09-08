using System.Net;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Service for proxying requests to Microsoft Graph API
/// </summary>
public interface IGraphProxyService
{
    /// <summary>
    /// Forward an HTTP request to Microsoft Graph API
    /// </summary>
    Task<(HttpStatusCode StatusCode, string Content, string ContentType, Dictionary<string, IEnumerable<string>> Headers)> 
        ForwardRequestAsync(
            string method, 
            string path, 
            string queryString, 
            IDictionary<string, IEnumerable<string>> requestHeaders, 
            Stream requestBody, 
            string correlationId);
}
