using GraphScopeProxy.Core.Configuration;
using GraphScopeProxy.Core.Services;
using GraphScopeProxy.Api.Middleware;
using Microsoft.Graph;
using Azure.Identity;
using Serilog;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File("logs/proxy-.log", 
            rollingInterval: RollingInterval.Day));

// Configure strongly-typed options
builder.Services.Configure<GraphScopeOptions>(
    builder.Configuration.GetSection(GraphScopeOptions.SectionName));

var graphScopeOptions = builder.Configuration
    .GetSection(GraphScopeOptions.SectionName)
    .Get<GraphScopeOptions>() ?? new GraphScopeOptions();

// Validate required configuration
if (string.IsNullOrEmpty(graphScopeOptions.TenantId) ||
    string.IsNullOrEmpty(graphScopeOptions.ClientId) ||
    string.IsNullOrEmpty(graphScopeOptions.ClientSecret) ||
    (string.IsNullOrEmpty(graphScopeOptions.JwtSigningKey) && string.IsNullOrEmpty(graphScopeOptions.JwtSecret)))
{
    throw new InvalidOperationException("Missing required GraphScope configuration. Please check TenantId, ClientId, ClientSecret, and JwtSigningKey/JwtSecret.");
}

// Register Microsoft Graph SDK
builder.Services.AddSingleton<GraphServiceClient>(provider =>
{
    // In development/demo mode with dummy credentials, create a mock client
    if (graphScopeOptions.TenantId.StartsWith("demo-") || 
        graphScopeOptions.ClientId.StartsWith("demo-"))
    {
        // For development, we'll create a GraphServiceClient with dummy credentials
        // The services will handle mock responses when Graph calls fail
        try
        {
            var dummyCredential = new ClientSecretCredential(
                "common", // Use common tenant for demo
                graphScopeOptions.ClientId,
                graphScopeOptions.ClientSecret);
            
            return new GraphServiceClient(dummyCredential);
        }
        catch
        {
            // If credential creation fails, return a basic client
            var fallbackCredential = new ClientSecretCredential(
                "00000000-0000-0000-0000-000000000000",
                "00000000-0000-0000-0000-000000000000",
                "dummy-secret");
            return new GraphServiceClient(fallbackCredential);
        }
    }
    
    var credential = new ClientSecretCredential(
        graphScopeOptions.TenantId,
        graphScopeOptions.ClientId,
        graphScopeOptions.ClientSecret);
    
    return new GraphServiceClient(credential);
});

// Register core services with Graph API integration
builder.Services.AddScoped<IGraphApiService, GraphApiService>();
builder.Services.AddScoped<IGraphTokenService, GraphTokenService>();
builder.Services.AddScoped<IResourceClassifier, ResourceClassifier>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
builder.Services.AddScoped<IGraphProxyService, GraphProxyService>();

// Register caching services
if (!string.IsNullOrEmpty(graphScopeOptions.RedisConnectionString))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = graphScopeOptions.RedisConnectionString;
    });
    builder.Services.AddSingleton<IScopeCache, RedisScopeCache>();
}
else
{
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<IScopeCache, MemoryScopeCache>();
}

// Add HTTP client for proxy calls
builder.Services.AddHttpClient("GraphProxy", client =>
{
    client.BaseAddress = new Uri("https://graph.microsoft.com/");
    client.DefaultRequestHeaders.Add("User-Agent", "GraphScopeProxy/1.0");
});

// Configure JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = !string.IsNullOrEmpty(graphScopeOptions.JwtSecret) 
            ? graphScopeOptions.JwtSecret 
            : graphScopeOptions.JwtSigningKey;
            
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidIssuer = graphScopeOptions.JwtIssuer,
            ValidAudience = graphScopeOptions.JwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("JWT authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

// Add controllers and API services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger/OpenAPI
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "GraphScope Proxy API",
        Version = "v1",
        Description = "A secure proxy for Microsoft Graph API with group-based resource scoping"
    });

    // Add JWT authentication to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });

    // Include XML comments
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    var coreXmlFile = "GraphScopeProxy.Core.xml";
    var coreXmlPath = Path.Combine(AppContext.BaseDirectory, coreXmlFile);
    if (File.Exists(coreXmlPath))
    {
        c.IncludeXmlComments(coreXmlPath);
    }
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy());
    // TODO: Add GraphHealthCheck when ready
    // .AddCheck<GraphHealthCheck>("graph");

if (!string.IsNullOrEmpty(graphScopeOptions.RedisConnectionString))
{
    builder.Services.AddHealthChecks()
        .AddRedis(graphScopeOptions.RedisConnectionString, name: "redis");
}

// Configure CORS if needed
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder.AllowAnyOrigin()
               .AllowAnyMethod()
               .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure middleware pipeline
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            var tokenId = httpContext.User.FindFirst("tid")?.Value;
            var groupId = httpContext.User.FindFirst("gid")?.Value;
            
            if (!string.IsNullOrEmpty(tokenId))
                diagnosticContext.Set("TokenId", tokenId);
            if (!string.IsNullOrEmpty(groupId))
                diagnosticContext.Set("GroupId", groupId);
        }
    };
});

// Add custom middleware
app.UseMiddleware<ErrorHandlingMiddleware>();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "GraphScope Proxy API V1");
        c.RoutePrefix = string.Empty; // Serve swagger at root
    });
}

app.UseHttpsRedirection();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// Add resource scope middleware after authentication
app.UseMiddleware<ResourceScopeMiddleware>();

app.MapControllers();

// Health check endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/admin/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(x => new
            {
                name = x.Key,
                status = x.Value.Status.ToString(),
                description = x.Value.Description,
                duration = x.Value.Duration.TotalMilliseconds
            }),
            timestamp = DateTime.UtcNow
        };
        
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response));
    }
});

// Add a simple version endpoint
app.MapGet("/admin/version", () => new
{
    version = "1.0.0",
    build = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
    environment = app.Environment.EnvironmentName
});

try
{
    Log.Information("Starting GraphScope Proxy");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
