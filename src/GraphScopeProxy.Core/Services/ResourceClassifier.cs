using GraphScopeProxy.Core.Models;
using GraphScopeProxy.Core.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.Models;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Service for classifying resources and determining allowed resources using Microsoft Graph
/// </summary>
public class ResourceClassifier : IResourceClassifier
{
    private readonly GraphScopeOptions _options;
    private readonly IGraphApiService _graphApiService;
    private readonly ILogger<ResourceClassifier> _logger;

    public ResourceClassifier(
        IOptions<GraphScopeOptions> options,
        IGraphApiService graphApiService,
        ILogger<ResourceClassifier> logger)
    {
        _options = options.Value;
        _graphApiService = graphApiService;
        _logger = logger;
    }

    public async Task<List<AllowedResource>> GetAllowedResourcesAsync(string groupId)
    {
        _logger.LogInformation("Getting allowed resources for group {GroupId}", groupId);

        try
        {
            // Get group members from Graph API
            var groupMembers = await _graphApiService.GetGroupMembersAsync(groupId);
            _logger.LogInformation("Found {Count} members in group {GroupId}", groupMembers.Count, groupId);

            var allowedResources = new List<AllowedResource>();

            // Process each group member and classify as resource
            foreach (var member in groupMembers)
            {
                var resource = await ClassifyGroupMemberAsync(member);
                if (resource != null && IsResourceTypeAllowed(resource.Type))
                {
                    allowedResources.Add(resource);
                    _logger.LogDebug("Added resource {ResourceId} of type {ResourceType}", 
                        resource.Id, resource.Type);
                }
            }

            // Optionally supplement with Graph Places API data
            if (_options.UseGraphPlacesApi)
            {
                await SupplementWithPlacesApiAsync(allowedResources);
            }

            // Apply scope size limit
            if (allowedResources.Count > _options.MaxScopeSize)
            {
                _logger.LogWarning("Resource scope size {Count} exceeds limit {MaxSize} for group {GroupId}. Truncating.", 
                    allowedResources.Count, _options.MaxScopeSize, groupId);
                allowedResources = allowedResources.Take(_options.MaxScopeSize).ToList();
            }

            _logger.LogInformation("Found {Count} allowed resources for group {GroupId}", 
                allowedResources.Count, groupId);

            return allowedResources;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get allowed resources for group {GroupId}", groupId);
            
            // Return empty list rather than throwing to avoid breaking authentication
            _logger.LogWarning("Returning empty resource list due to error for group {GroupId}", groupId);
            return new List<AllowedResource>();
        }
    }

    /// <summary>
    /// Classifies a Graph directory object as an allowed resource
    /// </summary>
    private Task<AllowedResource?> ClassifyGroupMemberAsync(DirectoryObject member)
    {
        try
        {
            // Extract basic properties
            var id = member.Id ?? "";
            var mail = "";
            var displayName = "";

            // Handle different member types
            if (member is User user)
            {
                mail = user.Mail ?? user.UserPrincipalName ?? "";
                displayName = user.DisplayName ?? "";
            }
            else if (member is Group group)
            {
                mail = group.Mail ?? "";
                displayName = group.DisplayName ?? "";
            }
            else
            {
                // For other directory object types, try to get basic properties
                if (member.AdditionalData?.TryGetValue("mail", out var mailValue) == true)
                    mail = mailValue?.ToString() ?? "";
                
                if (member.AdditionalData?.TryGetValue("displayName", out var nameValue) == true)
                    displayName = nameValue?.ToString() ?? "";
            }

            // Skip members without email addresses (likely not resources)
            if (string.IsNullOrEmpty(mail))
            {
                _logger.LogDebug("Skipping member {MemberId} - no email address", id);
                return Task.FromResult<AllowedResource?>(null);
            }

            // Classify the resource type and extract additional properties
            var (resourceType, capacity, location) = ClassifyResource(mail, displayName, null);

            // Skip unknown/disallowed resource types
            if (resourceType == ResourceType.Generic && !_options.AllowGenericResources)
            {
                _logger.LogDebug("Skipping member {Mail} - generic resource type not allowed", mail);
                return Task.FromResult<AllowedResource?>(null);
            }

            var result = new AllowedResource
            {
                Id = id,
                Mail = mail.ToLowerInvariant(),
                DisplayName = displayName,
                Type = resourceType,
                Capacity = capacity,
                Location = location
            };

            return Task.FromResult<AllowedResource?>(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to classify member {MemberId}", member.Id);
            return Task.FromResult<AllowedResource?>(null);
        }
    }

    /// <summary>
    /// Supplements resource list with additional data from Graph Places API
    /// </summary>
    private async Task SupplementWithPlacesApiAsync(List<AllowedResource> resources)
    {
        try
        {
            _logger.LogInformation("Supplementing resources with Graph Places API data");
            
            var places = await _graphApiService.GetPlacesAsync();
            var placesByEmail = places
                .Where(p => !string.IsNullOrEmpty(p.Phone)) // Use phone as email for matching
                .ToDictionary(p => p.Phone!.ToLowerInvariant(), p => p);

            foreach (var resource in resources)
            {
                if (placesByEmail.TryGetValue(resource.Mail, out var place))
                {
                    // Update resource with Places API data - simplified since Place doesn't have Capacity
                    if (!string.IsNullOrEmpty(place.DisplayName) && string.IsNullOrEmpty(resource.DisplayName))
                        resource.DisplayName = place.DisplayName;

                    _logger.LogDebug("Enhanced resource {ResourceId} with Places API data", resource.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to supplement resources with Places API data");
            // Continue without Places API data
        }
    }

    /// <summary>
    /// Classifies a resource based on email and display name patterns
    /// </summary>
    public (ResourceType Type, int? Capacity, string? Location) ClassifyResource(string email, string? displayName = null, string? description = null)
    {
        var originalName = displayName ?? "";
        var name = originalName.ToLowerInvariant();
        var emailAddress = email.ToLowerInvariant();

        _logger.LogDebug("Classifying resource with name '{DisplayName}' and mail '{Mail}'", 
            displayName, email);

        // Equipment detection (check first for more specific matches)
        if (name.Contains("equipment") || name.Contains("projector") ||
            name.Contains("device") || name.Contains("camera") ||
            name.Contains("tv") || name.Contains("screen") ||
            emailAddress.Contains("equipment"))
        {
            return (ResourceType.Equipment, null, ExtractLocation(originalName, email));
        }

        // Room detection heuristics
        if (name.Contains("room") || emailAddress.Contains("room") ||
            name.Contains("meeting") || name.Contains("conference") ||
            name.Contains("boardroom") || name.Contains("meetingroom"))
        {
            var capacity = ExtractCapacity(originalName);
            return (ResourceType.Room, capacity, ExtractLocation(originalName, email));
        }

        // Workspace detection
        if (name.Contains("workspace") || name.Contains("desk") ||
            name.Contains("office") || name.Contains("workstation"))
        {
            var capacity = ExtractCapacity(originalName);
            return (ResourceType.Workspace, capacity, ExtractLocation(originalName, email));
        }

        // Default to generic if allowed, otherwise room
        var defaultType = _options.AllowGenericResources ? ResourceType.Generic : ResourceType.Room;
        
        _logger.LogDebug("Resource classified as {ResourceType}", defaultType);
        
        return (defaultType, null, ExtractLocation(originalName, email));
    }

    /// <summary>
    /// Extracts capacity from resource name if present
    /// </summary>
    private static int? ExtractCapacity(string name)
    {
        // Look for patterns like "8-person", "seats-12", "cap-20", "(Cap: 10)", "(Capacity: 25)", "(8 people)", etc.
        var patterns = new[]
        {
            @"\(cap:?\s*(\d+)\)",           // (Cap: 10) or (cap 10)
            @"\(capacity:?\s*(\d+)\)",      // (Capacity: 25)
            @"\((\d+)\s+people?\)",         // (8 people)
            @"(\d+)[-\s]*person",           // 8-person
            @"seats?[-\s]*(\d+)",           // seats-12
            @"(\d+)[-\s]*seat"              // 8-seat
        };

        foreach (var pattern in patterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var capacity))
            {
                return capacity;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts location information from name or email
    /// </summary>
    private static string? ExtractLocation(string name, string email)
    {
        // Look for building/floor patterns and parenthetical location info
        var locationPatterns = new[]
        {
            @"\(([^)]+)\)$",                                // (Building B) at end
            @"-\s*(.+)$",                                   // - 1st Floor West Wing
            @"room\s+(.+)",                                 // Room Building A Floor 2
            @"building\s+([a-z]\s*[^,\s]*)",               // Building A Floor 2
            @"floor\s+(\d+[^,\s]*)",                       // Floor 2
            @"level\s+(\d+[^,\s]*)",                       // Level 3
            @"([a-z]+)\s+building",                        // A Building
            @"(\d+(?:st|nd|rd|th)\s+floor[^,]*)"          // 1st Floor West Wing
        };

        foreach (var pattern in locationPatterns)
        {
            var match = System.Text.RegularExpressions.Regex.Match(name, pattern, 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                // Get the original text from the matched position to preserve case
                var matchStart = match.Groups[1].Index;
                var matchLength = match.Groups[1].Length;
                var location = name.Substring(matchStart, matchLength).Trim();
                
                // Clean up the location string and preserve original case where possible
                if (!string.IsNullOrWhiteSpace(location))
                {
                    return location;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a resource type is allowed based on configuration
    /// </summary>
    public bool IsResourceTypeAllowed(ResourceType resourceType)
    {
        var allowedTypes = _options.AllowedPlaceTypes
            .Select(t => Enum.TryParse<ResourceType>(t, true, out var parsed) ? parsed : (ResourceType?)null)
            .Where(t => t.HasValue)
            .Select(t => t!.Value)
            .ToList();

        var isAllowed = allowedTypes.Contains(resourceType) ||
                       (resourceType == ResourceType.Generic && _options.AllowGenericResources);

        _logger.LogDebug("Resource type {ResourceType} is {Status}", 
            resourceType, isAllowed ? "allowed" : "not allowed");

        return isAllowed;
    }
}
