using GraphScopeProxy.Core.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GraphScopeProxy.Api;

/// <summary>
/// Health check for Microsoft Graph connectivity
/// </summary>
public class GraphHealthCheck : IHealthCheck
{
    private readonly IGraphTokenService _graphTokenService;
    private readonly ILogger<GraphHealthCheck> _logger;

    public GraphHealthCheck(IGraphTokenService graphTokenService, ILogger<GraphHealthCheck> logger)
    {
        _graphTokenService = graphTokenService;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var isValid = await _graphTokenService.IsTokenValidAsync();
            
            if (isValid)
            {
                _logger.LogDebug("Graph connectivity health check passed");
                return HealthCheckResult.Healthy("Graph API is accessible");
            }
            else
            {
                _logger.LogWarning("Graph connectivity health check failed - token invalid");
                return HealthCheckResult.Unhealthy("Graph API token is invalid");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Graph connectivity health check failed with exception");
            return HealthCheckResult.Unhealthy("Graph API connectivity failed", ex);
        }
    }
}
