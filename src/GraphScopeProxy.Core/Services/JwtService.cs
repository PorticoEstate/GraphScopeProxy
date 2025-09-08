using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using GraphScopeProxy.Core.Configuration;
using GraphScopeProxy.Core.Models;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// JWT service implementation for token generation and validation
/// </summary>
public class JwtService : IJwtService
{
    private readonly GraphScopeOptions _options;
    private readonly IMemoryCache _memoryCache;
    private readonly SigningCredentials _signingCredentials;
    private readonly TokenValidationParameters _tokenValidationParameters;

    public JwtService(IOptions<GraphScopeOptions> options, IMemoryCache memoryCache)
    {
        _options = options.Value;
        _memoryCache = memoryCache;

        // Create signing key from secret
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.JwtSecret ?? 
            throw new InvalidOperationException("JWT secret must be configured")));
        
        _signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        
        _tokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _options.JwtIssuer,
            ValidAudience = _options.JwtAudience,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.FromMinutes(5) // Allow 5 minutes clock skew
        };
    }

    public string GenerateToken(string apiKey, string groupId, IEnumerable<AllowedResource> allowedResources)
    {
        var jti = Guid.NewGuid().ToString();
        var now = DateTime.UtcNow;
        var expires = now.AddSeconds(_options.JwtExpirationSeconds);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, apiKey),
            new(JwtRegisteredClaimNames.Jti, jti),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("group_id", groupId),
            new("resource_count", allowedResources.Count().ToString())
        };

        // Add resource scopes as claims
        var resourcesJson = JsonSerializer.Serialize(allowedResources.Select(r => new
        {
            id = r.Id,
            display_name = r.DisplayName,
            type = r.Type.ToString().ToLowerInvariant(),
            location = r.Location
        }));
        
        claims.Add(new Claim("resources", resourcesJson));

        var token = new JwtSecurityToken(
            issuer: _options.JwtIssuer,
            audience: _options.JwtAudience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ResourceScope? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var principal = tokenHandler.ValidateToken(token, _tokenValidationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwtToken)
                return null;

            // Check if token is blacklisted
            var jti = principal.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
            if (jti != null && IsTokenBlacklistedAsync(jti).Result)
                return null;

            // Extract claims
            var groupId = principal.FindFirst("group_id")?.Value;
            var resourcesJson = principal.FindFirst("resources")?.Value;

            if (string.IsNullOrEmpty(groupId) || string.IsNullOrEmpty(resourcesJson))
                return null;

            // Deserialize resources
            var resourceData = JsonSerializer.Deserialize<JsonElement[]>(resourcesJson);
            var allowedResources = resourceData?.Select(r => new AllowedResource
            {
                Id = r.GetProperty("id").GetString() ?? "",
                DisplayName = r.GetProperty("display_name").GetString() ?? "",
                Type = Enum.Parse<ResourceType>(r.GetProperty("type").GetString() ?? "Unknown", true),
                Location = r.GetProperty("location").GetString()
            }).ToList() ?? new List<AllowedResource>();

            return new ResourceScope
            {
                GroupId = groupId,
                AllowedResources = allowedResources,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = jwtToken.ValidTo
            };
        }
        catch
        {
            return null;
        }
    }

    public Task<bool> InvalidateTokenAsync(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var jwt = tokenHandler.ReadJwtToken(token);
            var jti = jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Jti)?.Value;

            if (string.IsNullOrEmpty(jti))
                return Task.FromResult(false);

            // Add to blacklist cache with expiration
            var expirationTime = jwt.ValidTo.Subtract(DateTime.UtcNow);
            if (expirationTime > TimeSpan.Zero)
            {
                _memoryCache.Set($"blacklist:{jti}", true, expirationTime);
            }

            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<bool> IsTokenBlacklistedAsync(string jti)
    {
        try
        {
            var isBlacklisted = _memoryCache.TryGetValue($"blacklist:{jti}", out _);
            return Task.FromResult(isBlacklisted);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}
