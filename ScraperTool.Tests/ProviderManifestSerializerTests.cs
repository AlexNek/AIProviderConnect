using System.Text.Json.Nodes;

using AIProviderConnect.Models;

using FluentAssertions;

using ScraperTool.Services;

namespace ScraperTool.Tests;

public class ProviderManifestSerializerTests
{
    private static ProviderDefinition CreateDefinition(bool withDiscovery = false) => new()
    {
        Id = "test-provider",
        DisplayName = "Test Provider",
        BaseUrl = "https://test.example.com/v1",
        Protocol = EProviderProtocol.OpenAICompatible,
        HasModelDiscoveryApi = withDiscovery
    };

    private static ProviderResearchMetadata CreateResearch() => new()
    {
        Website = "https://test.example.com",
        LoginUrl = "https://test.example.com/login",
        ApiPricingUrl = "https://test.example.com/pricing",
        DocumentationUrl = "https://test.example.com/docs"
    };

    [Fact]
    public void Flatten_WithDefinitionOnly_ContainsDefinitionFields()
    {
        // Arrange
        var definition = CreateDefinition(withDiscovery: true);

        // Act
        var result = ProviderManifestSerializer.Flatten(definition, null);

        // Assert
        result["id"]!.GetValue<string>().Should().Be("test-provider");
        result["displayName"]!.GetValue<string>().Should().Be("Test Provider");
        result["baseUrl"]!.GetValue<string>().Should().Be("https://test.example.com/v1");
        result["protocol"]!.GetValue<string>().Should().Be("openaicompatible");
        result["hasModelDiscoveryApi"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void Flatten_WithDefinitionAndResearch_OverlaysResearchFields()
    {
        // Arrange
        var definition = CreateDefinition();

        var research = CreateResearch() with
        {
            HasFreeTier = true,
            MinModelCount = 42
        };

        // Act
        var result = ProviderManifestSerializer.Flatten(definition, research);

        // Assert — definition fields present
        result["id"]!.GetValue<string>().Should().Be("test-provider");

        // Assert — research fields overlaid on top
        result["website"]!.GetValue<string>().Should().Be("https://test.example.com");
        result["loginUrl"]!.GetValue<string>().Should().Be("https://test.example.com/login");
        result["apiPricingUrl"]!.GetValue<string>().Should().Be("https://test.example.com/pricing");
        result["documentationUrl"]!.GetValue<string>().Should().Be("https://test.example.com/docs");
        result["hasFreeTier"]!.GetValue<bool>().Should().BeTrue();
        result["minModelCount"]!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void Flatten_ResearchFieldNotInDefinition_AppearsInOutput()
    {
        // Arrange — research-only fields (e.g., "modelDiscoveryNotes") do not exist
        // on ProviderDefinition but must appear in the flattened output.
        var definition = CreateDefinition();

        var research = CreateResearch() with
        {
            ModelDiscoveryNotes = "Only text models supported"
        };

        // Act
        var result = ProviderManifestSerializer.Flatten(definition, research);

        // Assert
        result["modelDiscoveryNotes"]!.GetValue<string>().Should().Be("Only text models supported");
    }

    [Fact]
    public void Flatten_DoesNotMutateOriginalDefinition()
    {
        // Arrange
        var definition = CreateDefinition();
        var research = CreateResearch();

        // Act
        var result1 = ProviderManifestSerializer.Flatten(definition, research);
        var result2 = ProviderManifestSerializer.Flatten(definition, research);

        // Assert — both calls produce independent objects
        result1.Should().NotBeSameAs(result2);
        result1["id"]!.GetValue<string>().Should().Be(result2["id"]!.GetValue<string>());
    }

    [Fact]
    public void Flatten_WithEndpoints_RoundTripsThroughDefinition()
    {
        // Arrange — the manual editor's save path: Flatten serializes the definition
        // record, so a carried endpoints block must write through and survive a
        // ProviderDefinition round-trip.
        var definition = CreateDefinition() with
        {
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["decisions"] = new EndpointDefinition
                {
                    Path = "alpha/decisions",
                    BaseUrl = "https://api.test.example.com/api/",
                    Protocol = EProviderProtocol.Decision
                }
            }
        };

        // Act
        var flattened = ProviderManifestSerializer.Flatten(definition, CreateResearch());
        var json = flattened.ToJsonString();
        var roundTripped = System.Text.Json.JsonSerializer.Deserialize<ProviderDefinition>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        var entry = roundTripped!.Endpoints!["decisions"]!;
        entry.Path.Should().Be("alpha/decisions");
        entry.BaseUrl.Should().Be("https://api.test.example.com/api/");
        entry.Protocol.Should().Be(EProviderProtocol.Decision);
    }

    [Fact]
    public void Flatten_WithoutEndpoints_WritesNoEndpointsMember()
    {
        // Arrange & Act
        var flattened = ProviderManifestSerializer.Flatten(CreateDefinition(), CreateResearch());

        // Assert
        flattened.ContainsKey("endpoints").Should().BeFalse();
    }
}
