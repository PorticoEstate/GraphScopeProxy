# GraphScope Proxy - .NET Core Architecture

This document translates the original PHP architecture to .NET Core, maintaining all core concepts while leveraging .NET's ecosystem advantages.

---

## 📦 Technology Stack (.NET Translation)

| Component | PHP Original | .NET Core | Benefits |
|-----------|-------------|-----------|----------|
| Runtime | PHP 8.4 | .NET 8+ | Better performance, type safety |
| Framework | Slim 4 | ASP.NET Core | Robust middleware pipeline |
| HTTP Client | GuzzleHTTP | HttpClient + Graph SDK | Official Microsoft SDK |
| Authentication | Manual JWT | ASP.NET Core Identity | Built-in JWT handling |
| Caching | APCu/Redis | IMemoryCache/Redis | First-class caching |
| Logging | Monolog | ILogger + Serilog | Structured logging built-in |
| Configuration | .env files | IConfiguration | Strongly-typed config |
| DI Container | Manual | Built-in DI | Comprehensive DI system |

---

## 🏗️ Project Structure (.NET)

```text
GraphScopeProxy/
├── src/
│   ├── GraphScopeProxy.Api/              # Web API project
│   │   ├── Controllers/
│   │   │   ├── AuthController.cs
│   │   │   ├── AdminController.cs
│   │   │   └── ProxyController.cs
│   │   ├── Middleware/
│   │   │   ├── AuthenticationMiddleware.cs
│   │   │   ├── ResourceScopeMiddleware.cs
│   │   │   ├── LoggingMiddleware.cs
│   │   │   └── ErrorHandlingMiddleware.cs
│   │   ├── Program.cs
│   │   └── appsettings.json
│   ├── GraphScopeProxy.Core/             # Business logic
│   │   ├── Models/
│   │   │   ├── ResourceScope.cs
│   │   │   ├── AllowedResource.cs
│   │   │   ├── LoginRequest.cs
│   │   │   └── LoginResponse.cs
│   │   ├── Services/
│   │   │   ├── IGraphTokenService.cs
│   │   │   ├── GraphTokenService.cs
│   │   │   ├── IResourceClassifier.cs
│   │   │   ├── ResourceClassifier.cs
│   │   │   ├── IScopeCache.cs
│   │   │   ├── MemoryScopeCache.cs
│   │   │   ├── RedisScopeCache.cs
│   │   │   └── GraphProxyService.cs
│   │   └── Configuration/
│   │       └── GraphScopeOptions.cs
│   └── GraphScopeProxy.Tests/            # Test projects
│       ├── Unit/
│       ├── Integration/
│       └── GraphScopeProxy.Tests.csproj
├── docker/
│   ├── Dockerfile
│   └── docker-compose.yml
├── GraphScopeProxy.sln
└── README.md
```

---

## 🔧 Key Components Translation

### 1. Program.cs (Entry Point)

```csharp
using GraphScopeProxy.Core.Configuration;
using GraphScopeProxy.Core.Services;
using GraphScopeProxy.Api.Middleware;
using Microsoft.Graph;
using Azure.Identity;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// Configure strongly-typed options
builder.Services.Configure<GraphScopeOptions>(
    builder.Configuration.GetSection("GraphScope"));

// Register Microsoft Graph SDK
builder.Services.AddSingleton<GraphServiceClient>(provider =>
{
    var options = provider.GetRequiredService<IOptions<GraphScopeOptions>>().Value;
    var credential = new ClientSecretCredential(
        options.TenantId, 
        options.ClientId, 
        options.ClientSecret);
    return new GraphServiceClient(credential);
});

// Register services
builder.Services.AddScoped<IGraphTokenService, GraphTokenService>();
builder.Services.AddScoped<IResourceClassifier, ResourceClassifier>();
builder.Services.AddSingleton<IScopeCache, MemoryScopeCache>();
builder.Services.AddScoped<GraphProxyService>();

// Add memory caching
builder.Services.AddMemoryCache();

// Add HTTP client for proxy calls
builder.Services.AddHttpClient("GraphProxy", client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/");
});

// Configure JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["GraphScope:JwtSigningKey"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Configure middleware pipeline
app.UseMiddleware<LoggingMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.UseAuthentication();
app.UseMiddleware<ResourceScopeMiddleware>();

app.MapControllers();
app.MapHealthChecks("/admin/health");

app.Run();
```

### 2. Configuration Model

```csharp
// GraphScopeOptions.cs
namespace GraphScopeProxy.Core.Configuration;

public class GraphScopeOptions
{
    public const string SectionName = "GraphScope";

    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string JwtSigningKey { get; set; } = string.Empty;
    
    public List<string> AllowedPlaceTypes { get; set; } = new() { "room", "workspace" };
    public bool AllowGenericResources { get; set; } = false;
    public int ScopeCacheTtlSeconds { get; set; } = 900;
    public int MaxScopeSize { get; set; } = 500;
    public bool RequireGroupAlias { get; set; } = false;
    
    public List<string> ApiKeys { get; set; } = new();
}
```

### 3. Auth Controller

```csharp
// AuthController.cs
[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly IGraphTokenService _graphTokenService;
    private readonly IResourceClassifier _resourceClassifier;
    private readonly IScopeCache _scopeCache;
    private readonly IOptions<GraphScopeOptions> _options;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IGraphTokenService graphTokenService,
        IResourceClassifier resourceClassifier,
        IScopeCache scopeCache,
        IOptions<GraphScopeOptions> options,
        ILogger<AuthController> logger)
    {
        _graphTokenService = graphTokenService;
        _resourceClassifier = resourceClassifier;
        _scopeCache = scopeCache;
        _options = options;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        // Validate API key
        if (!_options.Value.ApiKeys.Contains(request.ApiKey))
        {
            _logger.LogWarning("Invalid API key provided");
            return Unauthorized("Invalid API key");
        }

        try
        {
            // Get group members and classify resources
            var allowedResources = await _resourceClassifier
                .GetAllowedResourcesAsync(request.GroupId);

            if (allowedResources.Count == 0)
            {
                _logger.LogWarning("No resources found for group {GroupId}", request.GroupId);
                return BadRequest("No accessible resources found for group");
            }

            if (allowedResources.Count > _options.Value.MaxScopeSize)
            {
                _logger.LogWarning("Group {GroupId} exceeds max scope size: {Count}", 
                    request.GroupId, allowedResources.Count);
                return BadRequest("Group size exceeds maximum allowed scope");
            }

            // Generate unique token ID and cache the scope
            var tokenId = GenerateSecureTokenId();
            var scope = new ResourceScope
            {
                TokenId = tokenId,
                GroupId = request.GroupId,
                AllowedResources = allowedResources,
                CreatedAt = DateTime.UtcNow
            };

            await _scopeCache.SetAsync(tokenId, scope, 
                TimeSpan.FromSeconds(_options.Value.ScopeCacheTtlSeconds));

            // Generate JWT
            var jwt = GenerateJwt(tokenId, request.GroupId, allowedResources.Count);

            var response = new LoginResponse
            {
                Token = jwt,
                GroupId = request.GroupId,
                ResourceCount = allowedResources.Count,
                ExpiresIn = _options.Value.ScopeCacheTtlSeconds
            };

            _logger.LogInformation("Login successful for group {GroupId} with {ResourceCount} resources", 
                request.GroupId, allowedResources.Count);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for group {GroupId}", request.GroupId);
            return StatusCode(500, "Internal server error during login");
        }
    }

    private string GenerateSecureTokenId()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private string GenerateJwt(string tokenId, string groupId, int resourceCount)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Value.JwtSigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim("tid", tokenId),
            new Claim("gid", groupId),
            new Claim("rc", resourceCount.ToString())
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddSeconds(_options.Value.ScopeCacheTtlSeconds),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

### 4. Resource Scope Middleware

```csharp
// ResourceScopeMiddleware.cs
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
        // Skip for auth and admin endpoints
        if (context.Request.Path.StartsWithSegments("/auth") || 
            context.Request.Path.StartsWithSegments("/admin"))
        {
            await _next(context);
            return;
        }

        // Extract token ID from JWT claims
        var tokenIdClaim = context.User.FindFirst("tid");
        if (tokenIdClaim == null)
        {
            _logger.LogWarning("No token ID found in JWT claims");
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

        // Check if request needs resource validation
        if (RequiresResourceValidation(context.Request.Path))
        {
            var resourceId = ExtractResourceId(context.Request.Path, context.Request.QueryString);
            if (!string.IsNullOrEmpty(resourceId) && !IsResourceAllowed(scope, resourceId))
            {
                _logger.LogWarning("Access denied to resource {ResourceId} for scope {TokenId}", 
                    resourceId, tokenIdClaim.Value);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Access denied to resource");
                return;
            }
        }

        // Intercept response for filtering if needed
        if (RequiresResponseFiltering(context.Request.Path))
        {
            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;

            await _next(context);

            // Filter response content
            responseBody.Seek(0, SeekOrigin.Begin);
            var responseContent = await new StreamReader(responseBody).ReadToEndAsync();
            var filteredContent = FilterResponseContent(responseContent, scope);

            context.Response.Body = originalBodyStream;
            await context.Response.WriteAsync(filteredContent);
        }
        else
        {
            await _next(context);
        }
    }

    private bool RequiresResourceValidation(PathString path)
    {
        // Check if path targets specific resources (calendars, events, etc.)
        var pathString = path.Value?.ToLowerInvariant() ?? "";
        return pathString.Contains("/calendars/") || 
               pathString.Contains("/events/") ||
               pathString.Contains("/calendar/");
    }

    private bool RequiresResponseFiltering(PathString path)
    {
        // Check if path returns lists that need filtering
        var pathString = path.Value?.ToLowerInvariant() ?? "";
        return pathString.Contains("/rooms") || 
               pathString.Contains("/places");
    }

    private string? ExtractResourceId(PathString path, QueryString query)
    {
        // Extract resource ID from URL path or query parameters
        // Implementation depends on specific Graph API patterns
        var pathParts = path.Value?.Split('/') ?? Array.Empty<string>();
        
        // Look for patterns like /calendars/{id} or /users/{id}/calendar
        for (int i = 0; i < pathParts.Length - 1; i++)
        {
            if (pathParts[i].Equals("calendars", StringComparison.OrdinalIgnoreCase))
            {
                return pathParts[i + 1];
            }
        }

        return null;
    }

    private bool IsResourceAllowed(ResourceScope scope, string resourceId)
    {
        return scope.AllowedResources.Any(r => 
            r.Id.Equals(resourceId, StringComparison.OrdinalIgnoreCase) ||
            r.Mail.Equals(resourceId, StringComparison.OrdinalIgnoreCase));
    }

    private string FilterResponseContent(string content, ResourceScope scope)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(content);
            
            // Handle different response types
            if (jsonDoc.RootElement.TryGetProperty("value", out var valueElement))
            {
                // Filter array responses (rooms, places lists)
                var filteredItems = new List<JsonElement>();
                
                foreach (var item in valueElement.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idElement) ||
                        item.TryGetProperty("emailAddress", out var emailElement))
                    {
                        var identifier = idElement.GetString() ?? emailElement.GetString();
                        if (!string.IsNullOrEmpty(identifier) && 
                            IsResourceAllowed(scope, identifier))
                        {
                            filteredItems.Add(item);
                        }
                    }
                }

                // Rebuild response with filtered items
                var filteredResponse = new
                {
                    value = filteredItems,
                    // Preserve other properties like @odata.nextLink if present
                };

                return JsonSerializer.Serialize(filteredResponse);
            }

            return content;
        }
        catch (JsonException)
        {
            // If not valid JSON, return as-is
            return content;
        }
    }
}
```

### 5. Graph Token Service

```csharp
// IGraphTokenService.cs & GraphTokenService.cs
public interface IGraphTokenService
{
    Task<string> GetAccessTokenAsync();
}

public class GraphTokenService : IGraphTokenService
{
    private readonly GraphServiceClient _graphClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GraphTokenService> _logger;

    public GraphTokenService(
        GraphServiceClient graphClient,
        IMemoryCache cache,
        ILogger<GraphTokenService> logger)
    {
        _graphClient = graphClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        const string cacheKey = "graph_access_token";
        
        if (_cache.TryGetValue(cacheKey, out string? cachedToken))
        {
            return cachedToken!;
        }

        try
        {
            // The GraphServiceClient handles token acquisition automatically
            // This is a simplified example - in practice, you might want to 
            // extract the token from the authentication provider
            var authProvider = _graphClient.RequestAdapter.AuthenticationProvider;
            
            // For this example, we'll make a simple call to ensure authentication
            await _graphClient.Me.GetAsync();
            
            // Token is now cached internally by the Graph SDK
            // For this implementation, we'll return a placeholder
            var token = "graph_sdk_managed_token";
            
            _cache.Set(cacheKey, token, TimeSpan.FromMinutes(55)); // Cache for 55 minutes
            
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Graph access token");
            throw;
        }
    }
}
```

### 6. Resource Classifier

```csharp
// IResourceClassifier.cs & ResourceClassifier.cs
public interface IResourceClassifier
{
    Task<List<AllowedResource>> GetAllowedResourcesAsync(string groupId);
}

public class ResourceClassifier : IResourceClassifier
{
    private readonly GraphServiceClient _graphClient;
    private readonly IOptions<GraphScopeOptions> _options;
    private readonly ILogger<ResourceClassifier> _logger;

    public ResourceClassifier(
        GraphServiceClient graphClient,
        IOptions<GraphScopeOptions> options,
        ILogger<ResourceClassifier> logger)
    {
        _graphClient = graphClient;
        _options = options;
        _logger = logger;
    }

    public async Task<List<AllowedResource>> GetAllowedResourcesAsync(string groupId)
    {
        var allowedResources = new List<AllowedResource>();

        try
        {
            // Get group members with pagination
            var members = await GetGroupMembersAsync(groupId);
            
            // Classify each member
            foreach (var member in members)
            {
                var resource = await ClassifyResourceAsync(member);
                if (resource != null && IsAllowedResourceType(resource.Type))
                {
                    allowedResources.Add(resource);
                }
            }

            // Optionally supplement with Places data
            if (_options.Value.AllowedPlaceTypes.Count > 0)
            {
                var places = await GetRelevantPlacesAsync(allowedResources);
                allowedResources.AddRange(places);
            }

            _logger.LogInformation("Classified {Count} allowed resources for group {GroupId}", 
                allowedResources.Count, groupId);

            return allowedResources.DistinctBy(r => r.Id).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get allowed resources for group {GroupId}", groupId);
            throw;
        }
    }

    private async Task<List<DirectoryObject>> GetGroupMembersAsync(string groupId)
    {
        var members = new List<DirectoryObject>();
        
        var membersPage = await _graphClient.Groups[groupId].Members
            .GetAsync(config => config.QueryParameters.Top = 100);

        if (membersPage?.Value != null)
        {
            members.AddRange(membersPage.Value);

            // Handle pagination
            while (membersPage.OdataNextLink != null)
            {
                var nextPage = await _graphClient.Groups[groupId].Members
                    .WithUrl(membersPage.OdataNextLink)
                    .GetAsync();
                
                if (nextPage?.Value != null)
                {
                    members.AddRange(nextPage.Value);
                    membersPage = nextPage;
                }
                else
                {
                    break;
                }
            }
        }

        return members;
    }

    private async Task<AllowedResource?> ClassifyResourceAsync(DirectoryObject member)
    {
        // Classification logic based on member properties
        if (member is User user && !string.IsNullOrEmpty(user.Mail))
        {
            var resourceType = DetermineResourceType(user);
            
            return new AllowedResource
            {
                Id = user.Id ?? string.Empty,
                Mail = user.Mail.ToLowerInvariant(),
                Type = resourceType,
                DisplayName = user.DisplayName
            };
        }

        return null;
    }

    private ResourceType DetermineResourceType(User user)
    {
        var displayName = user.DisplayName?.ToLowerInvariant() ?? "";
        var mail = user.Mail?.ToLowerInvariant() ?? "";

        // Room detection heuristics
        if (displayName.Contains("room") || mail.Contains("room") || 
            displayName.Contains("meeting") || displayName.Contains("conference"))
        {
            return ResourceType.Room;
        }

        // Workspace detection
        if (displayName.Contains("workspace") || displayName.Contains("desk") ||
            displayName.Contains("office"))
        {
            return ResourceType.Workspace;
        }

        // Equipment detection
        if (displayName.Contains("equipment") || displayName.Contains("projector") ||
            displayName.Contains("device"))
        {
            return ResourceType.Equipment;
        }

        return _options.Value.AllowGenericResources ? 
            ResourceType.Generic : ResourceType.Room; // Default fallback
    }

    private bool IsAllowedResourceType(ResourceType type)
    {
        var allowedTypes = _options.Value.AllowedPlaceTypes
            .Select(t => Enum.Parse<ResourceType>(t, true))
            .ToList();

        return allowedTypes.Contains(type) || 
               (type == ResourceType.Generic && _options.Value.AllowGenericResources);
    }

    private async Task<List<AllowedResource>> GetRelevantPlacesAsync(List<AllowedResource> existingResources)
    {
        // Supplement with Graph Places API if needed
        var places = new List<AllowedResource>();
        
        try
        {
            var placesResponse = await _graphClient.Places.GetAsync();
            
            if (placesResponse?.Value != null)
            {
                foreach (var place in placesResponse.Value)
                {
                    // Cross-reference with existing resources and add relevant ones
                    if (place is Room room && !string.IsNullOrEmpty(room.EmailAddress))
                    {
                        var existingResource = existingResources
                            .FirstOrDefault(r => r.Mail.Equals(room.EmailAddress, StringComparison.OrdinalIgnoreCase));
                        
                        if (existingResource != null)
                        {
                            // Enrich existing resource with place data
                            existingResource.DisplayName = room.DisplayName ?? existingResource.DisplayName;
                            // Add other place properties as needed
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch places data");
        }

        return places;
    }
}
```

### 7. Models

```csharp
// Models/LoginRequest.cs
public class LoginRequest
{
    public string ApiKey { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public string? GroupAlias { get; set; }
}

// Models/LoginResponse.cs
public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public int ResourceCount { get; set; }
    public int ExpiresIn { get; set; }
}

// Models/AllowedResource.cs
public class AllowedResource
{
    public string Id { get; set; } = string.Empty;
    public string Mail { get; set; } = string.Empty;
    public ResourceType Type { get; set; }
    public string? DisplayName { get; set; }
    public int? Capacity { get; set; }
}

// Models/ResourceScope.cs
public class ResourceScope
{
    public string TokenId { get; set; } = string.Empty;
    public string GroupId { get; set; } = string.Empty;
    public List<AllowedResource> AllowedResources { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

// Models/ResourceType.cs
public enum ResourceType
{
    Room,
    Workspace,
    Equipment,
    Generic
}
```

---

## 🔄 Key Improvements Over PHP Version

### 1. **Type Safety**

- Strongly-typed configuration via `IOptions<T>`
- Compile-time checking for all models and services
- IntelliSense support throughout

### 2. **Built-in Features**

- Native JWT authentication middleware
- Comprehensive logging with `ILogger`
- Built-in dependency injection
- Health checks out of the box

### 3. **Microsoft Graph Integration**

- Official Graph SDK with automatic token handling
- Built-in retry policies and error handling
- Type-safe Graph API calls

### 4. **Performance**

- Async/await throughout for better scalability
- Efficient memory usage with streaming
- Built-in response compression

### 5. **Observability**

- Structured logging with Serilog
- Built-in metrics with `IMetrics`
- Application Insights integration ready

---

## 📋 Configuration (appsettings.json)

```json
{
  "GraphScope": {
    "TenantId": "your-tenant-id",
    "ClientId": "your-client-id",
    "ClientSecret": "your-client-secret",
    "JwtSigningKey": "your-jwt-signing-key",
    "AllowedPlaceTypes": ["room", "workspace", "equipment"],
    "AllowGenericResources": false,
    "ScopeCacheTtlSeconds": 900,
    "MaxScopeSize": 500,
    "RequireGroupAlias": false,
    "ApiKeys": ["api-key-1", "api-key-2"]
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

---

## 🐳 Docker Configuration

```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/GraphScopeProxy.Api/GraphScopeProxy.Api.csproj", "src/GraphScopeProxy.Api/"]
COPY ["src/GraphScopeProxy.Core/GraphScopeProxy.Core.csproj", "src/GraphScopeProxy.Core/"]
RUN dotnet restore "src/GraphScopeProxy.Api/GraphScopeProxy.Api.csproj"

COPY . .
WORKDIR "/src/src/GraphScopeProxy.Api"
RUN dotnet build "GraphScopeProxy.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GraphScopeProxy.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "GraphScopeProxy.Api.dll"]
```

This .NET Core architecture maintains all your original concepts while providing better performance, type safety, and integration with the Microsoft ecosystem. Would you like me to elaborate on any specific component or create the actual project files?
