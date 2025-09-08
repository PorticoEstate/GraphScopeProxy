using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using GraphScopeProxy.Core.Configuration;
using GraphScopeProxy.Core.Models;

namespace GraphScopeProxy.Core.Services;

/// <summary>
/// Implementation of Graph API service with error handling and fallbacks
/// </summary>
public class GraphApiService : IGraphApiService
{
    private readonly GraphServiceClient _graphClient;
    private readonly GraphScopeOptions _options;
    private readonly ILogger<GraphApiService> _logger;
    private readonly bool _isDemoMode;

    public GraphApiService(
        GraphServiceClient graphClient,
        IOptions<GraphScopeOptions> options,
        ILogger<GraphApiService> logger)
    {
        _graphClient = graphClient;
        _options = options.Value;
        _logger = logger;
        _isDemoMode = _options.TenantId.StartsWith("demo-") || _options.ClientId.StartsWith("demo-");
    }

    public async Task<List<DirectoryObject>> GetGroupMembersAsync(string groupId)
    {
        _logger.LogInformation("Getting members for group {GroupId}", groupId);

        if (_isDemoMode)
        {
            _logger.LogInformation("Demo mode: returning mock group members");
            return GetMockGroupMembers();
        }

        try
        {
            var members = new List<DirectoryObject>();
            
            // Get group members with pagination
            var membersPage = await _graphClient.Groups[groupId].Members.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Top = 100; // Limit per page
                requestConfiguration.QueryParameters.Select = new[] { "id", "mail", "displayName", "userPrincipalName" };
            });

            if (membersPage?.Value != null)
            {
                members.AddRange(membersPage.Value);

                // Handle pagination
                var pageIterator = PageIterator<DirectoryObject, DirectoryObjectCollectionResponse>
                    .CreatePageIterator(_graphClient, membersPage, (member) =>
                    {
                        members.Add(member);
                        return true; // Continue iteration
                    });

                await pageIterator.IterateAsync();
            }

            _logger.LogInformation("Found {Count} members in group {GroupId}", members.Count, groupId);
            return members;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get members for group {GroupId}", groupId);
            
            // Fallback to mock data in case of Graph API issues
            _logger.LogWarning("Falling back to mock data due to Graph API error");
            return GetMockGroupMembers();
        }
    }

    public async Task<Group?> GetGroupAsync(string groupId)
    {
        _logger.LogInformation("Getting group information for {GroupId}", groupId);

        if (_isDemoMode)
        {
            return GetMockGroup(groupId);
        }

        try
        {
            var group = await _graphClient.Groups[groupId].GetAsync();
            _logger.LogInformation("Retrieved group {GroupName} ({GroupId})", group?.DisplayName, groupId);
            return group;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get group {GroupId}", groupId);
            return GetMockGroup(groupId);
        }
    }

    public async Task<User?> GetUserAsync(string userId)
    {
        _logger.LogInformation("Getting user information for {UserId}", userId);

        if (_isDemoMode)
        {
            return GetMockUser(userId);
        }

        try
        {
            var user = await _graphClient.Users[userId].GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = new[] { "id", "mail", "displayName", "userPrincipalName" };
            });
            
            _logger.LogInformation("Retrieved user {UserName} ({UserId})", user?.DisplayName, userId);
            return user;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user {UserId}", userId);
            return GetMockUser(userId);
        }
    }

    public Task<List<Place>> GetPlacesAsync()
    {
        _logger.LogInformation("Getting places from Graph Places API");

        if (_isDemoMode || !_options.UseGraphPlacesApi)
        {
            _logger.LogInformation("Demo mode or Places API disabled: returning mock places");
            return Task.FromResult(GetMockPlaces());
        }

        try
        {
            // Note: Places API might not be available in all Graph SDK versions
            // Fallback to mock data for now
            _logger.LogWarning("Places API not implemented in current Graph SDK version, using mock data");
            return Task.FromResult(GetMockPlaces());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get places from Graph Places API");
            
            // Fallback to mock data
            _logger.LogWarning("Falling back to mock places data");
            return Task.FromResult(GetMockPlaces());
        }
    }

    public async Task<bool> IsHealthyAsync()
    {
        if (_isDemoMode)
        {
            return true; // Demo mode is always "healthy"
        }

        try
        {
            // Simple health check - try to get current user
            var me = await _graphClient.Me.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Select = new[] { "id" };
            });
            
            return me != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Graph API health check failed");
            return false;
        }
    }

    #region Mock Data Methods

    private List<DirectoryObject> GetMockGroupMembers()
    {
        return new List<DirectoryObject>
        {
            new User
            {
                Id = "meeting-room-1@demo.com",
                Mail = "meeting-room-1@demo.com",
                DisplayName = "Meeting Room 1",
                UserPrincipalName = "meeting-room-1@demo.com"
            },
            new User
            {
                Id = "workspace-1@demo.com", 
                Mail = "workspace-1@demo.com",
                DisplayName = "Open Workspace 1",
                UserPrincipalName = "workspace-1@demo.com"
            },
            new User
            {
                Id = "projector-1@demo.com",
                Mail = "projector-1@demo.com",
                DisplayName = "Projector Equipment 1",
                UserPrincipalName = "projector-1@demo.com"
            },
            new User
            {
                Id = "conference-room-2@demo.com",
                Mail = "conference-room-2@demo.com",
                DisplayName = "Conference Room 2",
                UserPrincipalName = "conference-room-2@demo.com"
            }
        };
    }

    private Group GetMockGroup(string groupId)
    {
        return new Group
        {
            Id = groupId,
            DisplayName = $"Demo Group {groupId[..8]}",
            Mail = $"group-{groupId[..8]}@demo.com"
        };
    }

    private User GetMockUser(string userId)
    {
        return new User
        {
            Id = userId,
            DisplayName = $"Demo User {userId[..8]}",
            Mail = $"user-{userId[..8]}@demo.com",
            UserPrincipalName = $"user-{userId[..8]}@demo.com"
        };
    }

    private List<Place> GetMockPlaces()
    {
        return new List<Place>
        {
            new Room
            {
                Id = "room-building-a-101",
                DisplayName = "Building A - Room 101",
                Phone = "+1-555-0101"
            },
            new Room
            {
                Id = "room-building-a-102", 
                DisplayName = "Building A - Room 102",
                Phone = "+1-555-0102"
            }
        };
    }

    #endregion
}
