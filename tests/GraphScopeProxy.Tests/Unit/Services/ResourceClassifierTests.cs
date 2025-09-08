using GraphScopeProxy.Core.Models;
using GraphScopeProxy.Core.Services;
using GraphScopeProxy.Core.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;

namespace GraphScopeProxy.Tests.Unit.Services;

public class ResourceClassifierTests
{
    private readonly Mock<ILogger<ResourceClassifier>> _mockLogger;
    private readonly Mock<IGraphApiService> _mockGraphApiService;
    private readonly IOptions<GraphScopeOptions> _options;
    private readonly ResourceClassifier _classifier;

    public ResourceClassifierTests()
    {
        _mockLogger = new Mock<ILogger<ResourceClassifier>>();
        _mockGraphApiService = new Mock<IGraphApiService>();
        
        var options = new GraphScopeOptions
        {
            AllowedPlaceTypes = new List<string> { "room", "workspace", "equipment" },
            AllowGenericResources = false
        };
        
        _options = Options.Create(options);
        _classifier = new ResourceClassifier(_options, _mockGraphApiService.Object, _mockLogger.Object);
    }

    [Theory]
    [InlineData("Conference Room A", "confroom-a@company.com", ResourceType.Room)]
    [InlineData("Meeting Room 101", "meeting101@company.com", ResourceType.Room)]
    [InlineData("Boardroom Executive", "boardroom@company.com", ResourceType.Room)]
    [InlineData("Workspace Desk 42", "desk42@company.com", ResourceType.Workspace)]
    [InlineData("Office Manager", "office-mgr@company.com", ResourceType.Workspace)]
    [InlineData("Projector Room A", "projector-a@company.com", ResourceType.Equipment)]
    [InlineData("Camera Studio", "camera@company.com", ResourceType.Equipment)]
    public void ClassifyResource_ShouldReturnCorrectType(string displayName, string mail, ResourceType expectedType)
    {
        // Act
        var (resourceType, capacity, location) = _classifier.ClassifyResource(mail, displayName);

        // Assert
        resourceType.Should().Be(expectedType);
        // Capacity and location can be tested separately if needed
    }

    [Fact]
    public void ClassifyResource_WithUnknownType_ShouldReturnRoom_WhenGenericNotAllowed()
    {
        // Arrange
        var displayName = "Unknown Resource";
        var mail = "unknown@company.com";

        // Act
        var (resourceType, capacity, location) = _classifier.ClassifyResource(mail, displayName);

        // Assert
        resourceType.Should().Be(ResourceType.Room);
    }

    [Fact]
    public void ClassifyResource_WithUnknownType_ShouldReturnGeneric_WhenGenericAllowed()
    {
        // Arrange
        var options = new GraphScopeOptions
        {
            AllowedPlaceTypes = new List<string> { "room" },
            AllowGenericResources = true
        };
        var classifier = new ResourceClassifier(Options.Create(options), _mockGraphApiService.Object, _mockLogger.Object);

        // Act
        var (resourceType, capacity, location) = classifier.ClassifyResource("unknown@company.com", "Unknown Resource");

        // Assert
        resourceType.Should().Be(ResourceType.Generic);
    }

    [Theory]
    [InlineData(ResourceType.Room, true)]
    [InlineData(ResourceType.Workspace, true)]
    [InlineData(ResourceType.Equipment, true)]
    [InlineData(ResourceType.Generic, false)]
    public void IsResourceTypeAllowed_ShouldReturnExpectedResult(ResourceType resourceType, bool expected)
    {
        // Act
        var result = _classifier.IsResourceTypeAllowed(resourceType);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void IsResourceTypeAllowed_WithGenericAllowed_ShouldReturnTrue()
    {
        // Arrange
        var options = new GraphScopeOptions
        {
            AllowedPlaceTypes = new List<string> { "room" },
            AllowGenericResources = true
        };
        var classifier = new ResourceClassifier(Options.Create(options), _mockGraphApiService.Object, _mockLogger.Object);

        // Act
        var result = classifier.IsResourceTypeAllowed(ResourceType.Generic);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("Conference Room A (Cap: 10)", 10)]
    [InlineData("Meeting Room (Capacity: 25)", 25)]
    [InlineData("Small Room (8 people)", 8)]
    [InlineData("Regular Room", null)]
    public void ClassifyResource_ShouldExtractCapacity(string displayName, int? expectedCapacity)
    {
        // Act
        var (_, capacity, _) = _classifier.ClassifyResource("room@company.com", displayName);

        // Assert
        capacity.Should().Be(expectedCapacity);
    }

    [Theory]
    [InlineData("Room Building A Floor 2", "Building A Floor 2")]
    [InlineData("Conference Room - 1st Floor West Wing", "1st Floor West Wing")]
    [InlineData("Meeting Room (Building B)", "Building B")]
    [InlineData("Simple Room", null)]
    public void ClassifyResource_ShouldExtractLocation(string displayName, string? expectedLocation)
    {
        // Act
        var (_, _, location) = _classifier.ClassifyResource("room@company.com", displayName);

        // Assert
        location.Should().Be(expectedLocation);
    }
}
