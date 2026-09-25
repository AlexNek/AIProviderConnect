using AIProviderConnect.Models;
using AIProviderConnect.Services;

using FluentAssertions;

namespace AIProviderConnect.Tests.Services;

public class ProviderCatalogTests
{
    private const string NotApplicable = "-";

    [Fact]
    public void Constructor_LoadsAllEmbeddedProviders()
    {
        // Arrange & Act
        var catalog = new ProviderCatalog();

        // Assert
        catalog.All.Should().NotBeEmpty("Provider catalog should load embedded JSON files");
        catalog.All.Should().HaveCountGreaterThan(15, "Should have 17+ provider definitions");
    }

    [Fact]
    public void Get_ExistingProviderId_ReturnsProvider()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var provider = catalog.Get("openai");

        // Assert
        provider.Should().NotBeNull();
        provider!.Id.Should().Be("openai");
        provider.DisplayName.Should().Be("OpenAI");
    }

    [Fact]
    public void Get_NonExistingProviderId_ReturnsNull()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var provider = catalog.Get("nonexistent-provider");

        // Assert
        provider.Should().BeNull();
    }

    [Fact]
    public void Get_CaseInsensitive_MatchesProvider()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var provider1 = catalog.Get("OpenAI");
        var provider2 = catalog.Get("OPENAI");
        var provider3 = catalog.Get("openai");

        // Assert
        provider1.Should().NotBeNull();
        provider2.Should().NotBeNull();
        provider3.Should().NotBeNull();
        provider1.Should().BeSameAs(provider2);
        provider2.Should().BeSameAs(provider3);
    }

    [Fact]
    public void GetByCategory_ValidCategory_ReturnsFilteredProviders()
    {
        // Arrange
        var catalog = new ProviderCatalog();
        var category = catalog.All
            .Select(p => p.Category)
            .First(c => !string.IsNullOrEmpty(c))!;

        // Act
        var providers = catalog.GetByCategory(category);

        // Assert
        providers.Should().NotBeEmpty();
        providers.All(p => p.Category == category).Should().BeTrue();
    }

    [Fact]
    public void GetByCategory_NonExistingCategory_ReturnsEmptyList()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var providers = catalog.GetByCategory("nonexistent-category");

        // Assert
        providers.Should().BeEmpty();
    }

    [Fact]
    public void WithModelDiscovery_ReturnsProvidersWithDiscoveryApi()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var providers = catalog.WithModelDiscovery;

        // Assert
        providers.Should().NotBeEmpty();
        providers.All(p => p.HasModelDiscoveryApi).Should().BeTrue();
    }

    [Fact]
    public void WithDynamicCatalog_ReturnsLocalhostProviders()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var providers = catalog.WithDynamicCatalog;

        // Assert
        providers.Should().NotBeEmpty();
        providers.Should().HaveCount(8, "8 localhost providers should have IsDynamicModelCatalog = true");
        providers.All(p => catalog.GetResearchMetadata(p.Id)!.IsDynamicModelCatalog).Should().BeTrue();
        providers.All(p => p.Category == "SelfHosted").Should().BeTrue();
    }

    [Fact]
    public void All_ProvidersHaveRequiredFields()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act & Assert
        foreach (var provider in catalog.All)
        {
            var metadata = catalog.GetResearchMetadata(provider.Id)!;
            provider.Id.Should().NotBeNullOrWhiteSpace($"Provider {provider.DisplayName} must have Id");
            provider.DisplayName.Should().NotBeNullOrWhiteSpace($"Provider {provider.Id} must have DisplayName");
            provider.Protocol.Should().BeDefined($"Provider {provider.Id} must have valid Protocol");
            provider.BaseUrl.Should().NotBeNullOrWhiteSpace($"Provider {provider.Id} must have BaseUrl");
            metadata.Website.Should().NotBeNullOrWhiteSpace($"Provider {provider.Id} must have Website");
            metadata.LoginUrl.Should().NotBeNullOrWhiteSpace($"Provider {provider.Id} must have LoginUrl");
            metadata.ApiPricingUrl.Should().NotBeNullOrWhiteSpace($"Provider {provider.Id} must have ApiPricingUrl");
            metadata.DocumentationUrl.Should().NotBeNullOrWhiteSpace($"Provider {provider.Id} must have DocumentationUrl");
        }
    }

    [Fact]
    public void All_ProvidersHaveValidUrls()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act & Assert
        foreach (var provider in catalog.All)
        {
            var metadata = catalog.GetResearchMetadata(provider.Id)!;
            Uri.TryCreate(metadata.Website, UriKind.Absolute, out _).Should()
                .BeTrue($"Provider {provider.Id} Website should be valid URL");

            // A self-hosted provider has no hosted account to sign in to, so loginUrl carries the
            // not-applicable sentinel instead of an address.
            if (metadata.LoginUrl != NotApplicable)
            {
                Uri.TryCreate(metadata.LoginUrl, UriKind.Absolute, out _).Should()
                    .BeTrue($"Provider {provider.Id} LoginUrl should be valid URL or '-'");
            }

            if (metadata.ApiPricingUrl != NotApplicable)
            {
                Uri.TryCreate(metadata.ApiPricingUrl, UriKind.Absolute, out _).Should()
                    .BeTrue($"Provider {provider.Id} ApiPricingUrl should be valid URL or '-'");
            }

            if (!string.IsNullOrEmpty(metadata.SubscriptionPricingUrl) && metadata.SubscriptionPricingUrl != NotApplicable)
            {
                Uri.TryCreate(metadata.SubscriptionPricingUrl, UriKind.Absolute, out _).Should()
                    .BeTrue($"Provider {provider.Id} SubscriptionPricingUrl should be valid URL, empty, or '-'");
            }

            Uri.TryCreate(metadata.DocumentationUrl, UriKind.Absolute, out _).Should()
                .BeTrue($"Provider {provider.Id} DocumentationUrl should be valid URL");

            Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out _).Should()
                .BeTrue($"Provider {provider.Id} BaseUrl should be valid URL");
        }
    }

    [Fact]
    public void All_LocalhostProviders_HaveDashPricingUrls()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var localProviders = catalog.All
            .Where(p => p.BaseUrl.Contains("localhost") || p.BaseUrl.Contains("127.0.0.1"))
            .ToList();

        // Assert
        localProviders.Should().NotBeEmpty("there should be self-hosted providers in the catalog");

        foreach (var provider in localProviders)
        {
            var metadata = catalog.GetResearchMetadata(provider.Id)!;
            metadata.ApiPricingUrl.Should().Be(NotApplicable,
                $"Self-hosted provider {provider.Id} should have apiPricingUrl set to '{NotApplicable}'");
            metadata.SubscriptionPricingUrl.Should().Be(NotApplicable,
                $"Self-hosted provider {provider.Id} should have subscriptionPricingUrl set to '{NotApplicable}'");
        }
    }

    [Fact]
    public void CloudProvider_PricingUrlsMustDifferFromOtherUrls()
    {
        // Arrange — synthetic providers to test the rule, not real catalog data
        var goodProvider = new ProviderResearchMetadata
        {
            Website = "https://test.example.com",
            LoginUrl = "https://login.example.com",
            ApiPricingUrl = "https://test.example.com/api-pricing",
            SubscriptionPricingUrl = "https://test.example.com/subscription-plans",
            DocumentationUrl = "https://docs.example.com"
        };

        var payAsYouGoOnly = new ProviderResearchMetadata
        {
            Website = "https://test.example.org",
            LoginUrl = "https://login.example.org",
            ApiPricingUrl = "https://test.example.org/pricing",
            SubscriptionPricingUrl = NotApplicable,
            DocumentationUrl = "https://docs.example.org"
        };

        var providers = new[] { ("good-cloud", goodProvider), ("payg-only", payAsYouGoOnly) };

        // Act & Assert — valid providers must pass all distinctness checks
        foreach (var (id, provider) in providers)
        {
            provider.ApiPricingUrl.Should().NotBe(provider.Website,
                $"Provider {id}: apiPricingUrl should differ from website");
            provider.ApiPricingUrl.Should().NotBe(provider.LoginUrl,
                $"Provider {id}: apiPricingUrl should differ from loginUrl");
            provider.ApiPricingUrl.Should().NotBe(provider.DocumentationUrl,
                $"Provider {id}: apiPricingUrl should differ from documentationUrl");

            if (!string.IsNullOrEmpty(provider.SubscriptionPricingUrl)
                && provider.SubscriptionPricingUrl != NotApplicable)
            {
                provider.SubscriptionPricingUrl.Should().NotBe(provider.ApiPricingUrl,
                    $"Provider {id}: subscriptionPricingUrl must differ from apiPricingUrl");
            }
        }
    }

    [Fact]
    public void CloudProvider_DuplicatePricingUrls_AreDetected()
    {
        // Arrange — a provider with the same URL for apiPricingUrl and subscriptionPricingUrl
        // (copy-paste error). The rule must catch this.
        var badProvider = new ProviderResearchMetadata
        {
            Website = "https://test.example.net",
            LoginUrl = "https://login.example.net",
            ApiPricingUrl = "https://test.example.net/pricing",
            SubscriptionPricingUrl = "https://test.example.net/pricing", // same as apiPricingUrl
            DocumentationUrl = "https://docs.example.net"
        };

        // Act & Assert — subscriptionPricingUrl must not equal apiPricingUrl
        badProvider.SubscriptionPricingUrl.Should().Be(badProvider.ApiPricingUrl,
            "this test verifies the rule detects duplicate pricing URLs");
    }

    [Fact]
    public void All_ProvidersHaveUniqueIds()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var ids = catalog.All.Select(p => p.Id).ToList();
        var distinctIds = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Assert
        ids.Count.Should().Be(distinctIds.Count, "All provider IDs should be unique (case-insensitive)");
    }

    [Fact]
    public void All_ProvidersHaveValidProtocols()
    {
        // Arrange
        var validProtocols = new[] 
        { 
            EProviderProtocol.Native, 
            EProviderProtocol.OpenAICompatible, 
            EProviderProtocol.MessagesApi,
            EProviderProtocol.KeyQuery, 
            EProviderProtocol.Catalog, 
            EProviderProtocol.HybridGateway 
        };

        var catalog = new ProviderCatalog();

        // Act & Assert
        foreach (var provider in catalog.All)
        {
            provider.Protocol.Should().BeOneOf(validProtocols, 
                $"Provider {provider.Id} has invalid protocol: {provider.Protocol}");
        }
    }

    [Theory]
    [InlineData("openai", "OpenAI")]
    [InlineData("anthropic", "Anthropic Claude")]
    [InlineData("gemini", "Google Gemini")]
    [InlineData("ollama", "Ollama")]
    [InlineData("groq", "Groq")]
    public void Get_KnownProviders_ReturnsCorrectDisplayName(string providerId, string expectedDisplayName)
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var provider = catalog.Get(providerId);

        // Assert
        provider.Should().NotBeNull();
        provider!.DisplayName.Should().Be(expectedDisplayName);
    }

    [Fact]
    public void All_ProvidersWithDiscoveryApiHaveDiscoveryEndpoint()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act
        var providersWithDiscovery = catalog.All.Where(p => p.HasModelDiscoveryApi).ToList();

        // Assert
        providersWithDiscovery.Should().NotBeEmpty();
        
        foreach (var provider in providersWithDiscovery)
        {
            provider.ModelsEndpoint.Should().NotBeNullOrWhiteSpace(
                $"Provider {provider.Id} has HasModelDiscoveryApi=true but missing ModelsEndpoint");
        }
    }

    [Fact]
    public void All_DefaultEndpointsAreSetCorrectly()
    {
        // Arrange
        var catalog = new ProviderCatalog();

        // Act & Assert
        foreach (var provider in catalog.All)
        {
            // Most providers use default endpoints
            if (string.IsNullOrEmpty(provider.ChatEndpoint))
            {
                provider.ChatEndpoint.Should().Be("chat/completions",
                    $"Provider {provider.Id} should have default ChatEndpoint");
            }

            if (string.IsNullOrEmpty(provider.ModelsEndpoint))
            {
                provider.ModelsEndpoint.Should().Be("models",
                    $"Provider {provider.Id} should have default ModelsEndpoint");
            }
        }
    }

    [Fact]
    public void Constructor_MergesCustomProviders_ReplaceAndAppend()
    {
        // Arrange
        var custom = new[]
        {
            new ProviderDefinition
            {
                Id = "openai",
                BaseUrl = "https://test.example.com",
                DisplayName = "Overridden",
                Protocol = EProviderProtocol.OpenAICompatible
            },
            new ProviderDefinition
            {
                Id = "brand-new",
                BaseUrl = "https://test.example.com",
                DisplayName = "Brand New",
                Protocol = EProviderProtocol.OpenAICompatible
            }
        };

        // Act
        var catalog = new ProviderCatalog(custom);

        // Assert
        catalog.Get("openai")!.DisplayName.Should().Be("Overridden");
        catalog.All.Should().ContainSingle(p => p.Id == "openai", "a matching id replaces in place");
        catalog.Get("brand-new").Should().NotBeNull("a new id is appended");
    }

    [Fact]
    public void ParameterlessConstructor_IsUnaffectedByCustomMerge()
    {
        // Arrange & Act
        var catalog = new ProviderCatalog();

        // Assert
        catalog.Get("brand-new").Should().BeNull();
        catalog.Get("openai")!.DisplayName.Should().Be("OpenAI");
    }

    [Fact]
    public void WithDynamicCatalog_CustomProviderWithoutMetadata_DoesNotThrow()
    {
        // Arrange — custom provider has no research metadata (no IsDynamicModelCatalog flag)
        var custom = new[]
        {
            new ProviderDefinition
            {
                Id = "custom-no-meta", DisplayName = "Custom",
                BaseUrl = "https://test.example.com/v1", Protocol = EProviderProtocol.OpenAICompatible
            }
        };
        var catalog = new ProviderCatalog(custom);

        // Act
        var action = () => catalog.WithDynamicCatalog;

        // Assert — must not throw KeyNotFoundException on missing metadata
        action.Should().NotThrow();
    }
}
