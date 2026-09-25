using System.Text.Json;

using AIProviderConnect.Models;

using FluentAssertions;

namespace AIProviderConnect.Tests.Models;

public class ProviderResearchMetadataTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void SerializeAndDeserialize_RoundTrip_PreservesAllFields()
    {
        // Arrange
        var original = new ProviderResearchMetadata
        {
            Website = "https://test-provider.com",
            LoginUrl = "https://test-provider.com/login",
            ApiPricingUrl = "https://test-provider.com/api-pricing",
            SubscriptionPricingUrl = "https://test-provider.com/plans",
            DocumentationUrl = "https://test-provider.com/docs",
            ModelDescription = "Test models",
            PayAsYouGo = true,
            PayAsYouGoDescription = "Pay per use",
            ModelDiscoveryNotes = "Standard OpenAI format",
            MinimumCommitment = "$0",
            HasFreeTier = true,
            SupportsFineTuning = false,
            MinModelCount = 42,
            IsDynamicModelCatalog = true,
            RegionalEndpoints = new Dictionary<string, string>
            {
                ["us"] = "https://us.test-provider.com",
                ["eu"] = "https://eu.test-provider.com"
            }
        };

        // Act
        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<ProviderResearchMetadata>(json, JsonOptions);

        // Assert
        deserialized.Should().NotBeNull();
        deserialized!.Website.Should().Be(original.Website);
        deserialized.LoginUrl.Should().Be(original.LoginUrl);
        deserialized.ApiPricingUrl.Should().Be(original.ApiPricingUrl);
        deserialized.SubscriptionPricingUrl.Should().Be(original.SubscriptionPricingUrl);
        deserialized.DocumentationUrl.Should().Be(original.DocumentationUrl);
        deserialized.ModelDescription.Should().Be(original.ModelDescription);
        deserialized.PayAsYouGo.Should().Be(original.PayAsYouGo);
        deserialized.PayAsYouGoDescription.Should().Be(original.PayAsYouGoDescription);
        deserialized.ModelDiscoveryNotes.Should().Be(original.ModelDiscoveryNotes);
        deserialized.MinimumCommitment.Should().Be(original.MinimumCommitment);
        deserialized.HasFreeTier.Should().Be(original.HasFreeTier);
        deserialized.SupportsFineTuning.Should().Be(original.SupportsFineTuning);
        deserialized.MinModelCount.Should().Be(original.MinModelCount);
        deserialized.IsDynamicModelCatalog.Should().Be(original.IsDynamicModelCatalog);
        deserialized.RegionalEndpoints.Should().BeEquivalentTo(original.RegionalEndpoints);
    }

    [Fact]
    public void Deserialize_CaseInsensitive_MatchesFields()
    {
        // Arrange
        var json = """
        {
            "website": "https://case-test.com",
            "loginurl": "https://case-test.com/login",
            "apipricingurl": "https://case-test.com/api-pricing",
            "subscriptionpricingurl": "https://case-test.com/plans",
            "documentationurl": "https://case-test.com/docs"
        }
        """;

        // Act
        var metadata = JsonSerializer.Deserialize<ProviderResearchMetadata>(json, JsonOptions);

        // Assert
        metadata.Should().NotBeNull();
        metadata!.Website.Should().Be("https://case-test.com");
        metadata.LoginUrl.Should().Be("https://case-test.com/login");
        metadata.ApiPricingUrl.Should().Be("https://case-test.com/api-pricing");
        metadata.SubscriptionPricingUrl.Should().Be("https://case-test.com/plans");
        metadata.DocumentationUrl.Should().Be("https://case-test.com/docs");
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var metadata = new ProviderResearchMetadata();

        // Assert
        metadata.Website.Should().BeNull();
        metadata.LoginUrl.Should().BeNull();
        metadata.ApiPricingUrl.Should().BeNull();
        metadata.SubscriptionPricingUrl.Should().BeNull();
        metadata.DocumentationUrl.Should().BeNull();
        metadata.ModelDescription.Should().BeNull();
        metadata.PayAsYouGo.Should().BeFalse();
        metadata.PayAsYouGoDescription.Should().BeNull();
        metadata.ModelDiscoveryNotes.Should().BeNull();
        metadata.MinimumCommitment.Should().BeNull();
        metadata.HasFreeTier.Should().BeFalse();
        metadata.SupportsFineTuning.Should().BeFalse();
        metadata.MinModelCount.Should().Be(0);
        metadata.IsDynamicModelCatalog.Should().BeFalse();
        metadata.RegionalEndpoints.Should().BeNull();
    }

    [Fact]
    public void Deserialize_WithUnknownFields_IgnoresThem()
    {
        // Arrange
        var json = """
        {
            "website": "https://test.com",
            "apiPricingUrl": "https://test.com/api-pricing",
            "subscriptionPricingUrl": "https://test.com/subscribe",
            "unknownField1": "value1",
            "unknownField2": 123,
            "unknownField3": true
        }
        """;

        // Act
        var metadata = JsonSerializer.Deserialize<ProviderResearchMetadata>(json, JsonOptions);

        // Assert
        metadata.Should().NotBeNull();
        metadata!.Website.Should().Be("https://test.com");
        metadata.ApiPricingUrl.Should().Be("https://test.com/api-pricing");
        metadata.SubscriptionPricingUrl.Should().Be("https://test.com/subscribe");
    }

    [Fact]
    public void MinModelCount_CanBeZero()
    {
        // Arrange & Act
        var metadata = new ProviderResearchMetadata { MinModelCount = 0 };

        // Assert
        metadata.MinModelCount.Should().Be(0);
    }

    [Fact]
    public void MinModelCount_CanBeLargeNumber()
    {
        // Arrange & Act
        var metadata = new ProviderResearchMetadata { MinModelCount = 10000 };

        // Assert
        metadata.MinModelCount.Should().Be(10000);
    }

    [Fact]
    public void BooleanFields_DefaultToFalse()
    {
        // Arrange
        var metadata = new ProviderResearchMetadata();

        // Assert
        metadata.PayAsYouGo.Should().BeFalse();
        metadata.HasFreeTier.Should().BeFalse();
        metadata.SupportsFineTuning.Should().BeFalse();
        metadata.IsDynamicModelCatalog.Should().BeFalse();
    }

    [Fact]
    public void BooleanFields_CanBeSetToTrue()
    {
        // Arrange & Act
        var metadata = new ProviderResearchMetadata
        {
            PayAsYouGo = true,
            HasFreeTier = true,
            SupportsFineTuning = true,
            IsDynamicModelCatalog = true
        };

        // Assert
        metadata.PayAsYouGo.Should().BeTrue();
        metadata.HasFreeTier.Should().BeTrue();
        metadata.SupportsFineTuning.Should().BeTrue();
        metadata.IsDynamicModelCatalog.Should().BeTrue();
    }
}
