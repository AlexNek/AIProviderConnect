using System.Reflection;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Constants;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moq;

namespace AIProviderConnect.Tests.DependencyInjection;

public class AIProviderRegistrationBuilderTests
{
    private static ProviderDefinition Definition(
        string id,
        string baseUrl = "https://test.example.com",
        EProviderProtocol protocol = EProviderProtocol.OpenAICompatible,
        string displayName = "Test Provider") =>
        new()
        {
            Id = id,
            BaseUrl = baseUrl,
            DisplayName = displayName,
            Protocol = protocol
        };

    [Fact]
    public void AddAiProviders_CustomNewId_ResolvesProviderFromContainer()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddLogging();

        // Act
        services.AddAiProviders(new[] { Definition("custom-new") });
        using var provider = services.BuildServiceProvider();

        // Assert
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("custom-new");
        resolved.Id.Should().Be("custom-new");
    }

    [Fact]
    public void AddAiProviders_HostWithoutLogging_ResolvesProviderFromContainer()
    {
        // Arrange — deliberately no AddLogging(): the library depends on Logging.Abstractions only,
        // so a bare container must still be able to construct providers.
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());

        // Act
        services.AddAiProviders(new[] { Definition("custom-new") });
        using var provider = services.BuildServiceProvider();

        // Assert
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("custom-new");
        resolved.Id.Should().Be("custom-new");
    }

    [Fact]
    public void AddAiProviders_HostWithLogging_PassesHostLoggerToProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddLogging();
        services.AddAiProviders(new[] { Definition("custom-new") });
        using var provider = services.BuildServiceProvider();
        var loggerProperty = typeof(AIProviderBase)
            .GetProperty("Logger", BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("custom-new");

        // Assert
        loggerProperty!.GetValue(resolved).Should().NotBeSameAs(NullLogger.Instance);
    }

    [Fact]
    public void Replace_EmbeddedId_ChangesCatalogMetadata()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAiProviders(
            b => b.Replace("openai", d => d with { DisplayName = "Overridden OpenAI" }));
        using var provider = services.BuildServiceProvider();

        // Assert
        var catalog = provider.GetRequiredService<ProviderCatalog>();
        catalog.Get("openai")!.DisplayName.Should().Be("Overridden OpenAI");
    }

    [Fact]
    public void Replace_OverridesBaseUrl_InCatalog()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAiProviders(
            b => b.Replace("openai", d => d with { BaseUrl = "https://test.example.com" }));
        using var provider = services.BuildServiceProvider();

        // Assert
        var catalog = provider.GetRequiredService<ProviderCatalog>();
        catalog.Get("openai")!.BaseUrl.Should().Be("https://test.example.com");
    }

    [Fact]
    public void Seeding_FillsBaseUrlFromDefinition_WhenOnlyApiKeyConfigured()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAiProviders(
            b => b
                .Add(Definition("seeded"))
                .Configure<OpenAICompatibleProviderOptions>("seeded", o => o.ApiKey = "fake-api-key"));
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsFactory<OpenAICompatibleProviderOptions>>()
            .Create("seeded");

        // Assert
        options.BaseUrl.Should().Be("https://test.example.com", "BaseUrl is seeded from the definition");
        options.ApiKey.Should().Be("fake-api-key");
        options.ChatEndpoint.Should().Be(EndpointDefaults.ChatCompletions);
    }

    [Fact]
    public void ConsumerConfigure_OverridesSeededValue()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddAiProviders(
            b => b
                .Add(Definition("seeded", baseUrl: "https://test.example.com"))
                .Configure<OpenAICompatibleProviderOptions>(
                    "seeded",
                    o => o.BaseUrl = "https://consumer.example.com"));
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsFactory<OpenAICompatibleProviderOptions>>()
            .Create("seeded");

        // Assert
        options.BaseUrl.Should().Be("https://consumer.example.com", "consumer Configure runs after seeding");
    }

    [Fact]
    public void AddProvider_RegistersCustomProvider_AndExcludesIdFromProtocolLoop()
    {
        // Arrange
        var custom = new Mock<IAIProvider>();
        custom.Setup(p => p.Id).Returns("mine");

        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());

        // Act — same id has both a definition and a custom factory; the loop must skip it.
        services.AddAiProviders(
            b => b
                .Add(Definition("mine"))
                .AddProvider<IAIProvider>("mine", _ => custom.Object));
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredKeyedService<IAIProvider>("mine");

        // Assert
        resolved.Should().BeSameAs(custom.Object, "the custom factory is used for the keyed registration");
    }

    [Fact]
    public void Validation_Throws_ForMissingId_MissingBaseUrl_AndDuplicateId()
    {
        // Arrange & Act
        Action missingId = () => new ServiceCollection().AddAiProviders(new[] { Definition("") });
        Action missingBaseUrl = () =>
            new ServiceCollection().AddAiProviders(new[] { Definition("x", baseUrl: "") });
        Action duplicateId = () =>
            new ServiceCollection().AddAiProviders(new[] { Definition("dup"), Definition("dup") });

        // Assert
        missingId.Should().Throw<ArgumentException>();
        missingBaseUrl.Should().Throw<ArgumentException>();
        duplicateId.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Parameterless_AddAiProviders_StillRegistersEmbeddedProviders()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddLogging();

        // Act
        services.AddAiProviders();
        using var provider = services.BuildServiceProvider();

        // Assert
        var openai = provider.GetRequiredKeyedService<IAIProvider>("openai");
        openai.Should().NotBeNull();
        provider.GetRequiredService<ProviderCatalog>().Get("openai").Should().NotBeNull();
    }

    [Fact]
    public void Replace_ChainedOnSameId_ComposesTransforms()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act — two transforms on the same embedded id must compose, not discard the first.
        services.AddAiProviders(
            b => b
                .Replace("openai", d => d with { DisplayName = "First" })
                .Replace("openai", d => d with { BaseUrl = "https://test.example.com" }));
        using var provider = services.BuildServiceProvider();

        // Assert
        var definition = provider.GetRequiredService<ProviderCatalog>().Get("openai")!;
        definition.DisplayName.Should().Be("First", "the first transform must survive the second");
        definition.BaseUrl.Should().Be("https://test.example.com");
    }

    [Fact]
    public void AddAiProviders_NativeProtocolWithoutCustomProvider_Throws()
    {
        // Arrange
        var services = new ServiceCollection();
        var native = Definition("native-only", protocol: EProviderProtocol.Native);

        // Act
        Action act = () => services.AddAiProviders(new[] { native });

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*Native*");
    }

    [Fact]
    public async Task OverrideModels_WrapsResolvedProvider_AndMergesCatalog()
    {
        // Arrange
        var live = new List<AIModel>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o", ProviderId = "mine" }
        };
        var custom = new Mock<IAIProvider>();
        custom.Setup(p => p.Id).Returns("mine");
        custom.As<IModelDiscoveryProvider>()
            .Setup(d => d.GetModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);

        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());

        // Act
        services.AddAiProviders(
            b => b
                .Add(Definition("mine"))
                .AddProvider<IAIProvider>("mine", _ => custom.Object)
                .OverrideModels(
                    "mine",
                    new[] { new ModelOverride { Id = "gpt-4o", PromptPrice = 2.50m } }));
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredKeyedService<IAIProvider>("mine");
        var models = await ((IModelDiscoveryProvider)resolved).GetModelsAsync();

        // Assert
        resolved.Should().BeOfType<NonStreamingModelCatalogOverrideDecorator>();
        models.Should().ContainSingle().Which.PromptPrice.Should().Be(2.50m);
    }

    [Fact]
    public async Task OverrideModels_StreamingInner_WrappedInStreamingDecorator()
    {
        // Arrange
        var live = new List<AIModel>
        {
            new() { Id = "gpt-4o", DisplayName = "GPT-4o", ProviderId = "mine" }
        };
        var custom = new Mock<IAIProvider>();
        custom.Setup(p => p.Id).Returns("mine");
        custom.As<IStreamingChatProvider>();
        custom.As<IModelDiscoveryProvider>()
            .Setup(d => d.GetModelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(live);

        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());

        // Act
        services.AddAiProviders(
            b => b
                .Add(Definition("mine"))
                .AddProvider<IAIProvider>("mine", _ => custom.Object)
                .OverrideModels(
                    "mine",
                    new[] { new ModelOverride { Id = "gpt-4o", PromptPrice = 2.50m } }));
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredKeyedService<IAIProvider>("mine");

        // Assert
        resolved.Should().BeOfType<ModelCatalogOverrideDecorator>();
        resolved.Should().BeAssignableTo<IStreamingChatProvider>("a streaming inner provider must remain streaming after decoration");
    }

    [Fact]
    public void NoOverrides_ResolvedProviderIsNotDecorated()
    {
        // Arrange
        var custom = new Mock<IAIProvider>();
        custom.Setup(p => p.Id).Returns("mine");

        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());

        // Act
        services.AddAiProviders(
            b => b
                .Add(Definition("mine"))
                .AddProvider<IAIProvider>("mine", _ => custom.Object));
        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredKeyedService<IAIProvider>("mine");

        // Assert
        resolved.Should().BeSameAs(custom.Object, "a provider with no overrides is never decorated");
    }

    [Fact]
    public void AddAiProviders_NativeProtocolWithCustomProvider_DoesNotThrow()
    {
        // Arrange
        var custom = new Mock<IAIProvider>();
        custom.Setup(p => p.Id).Returns("native-only");
        var services = new ServiceCollection();

        // Act
        Action act = () =>
            services.AddAiProviders(
                b => b
                    .Add(Definition("native-only", protocol: EProviderProtocol.Native))
                    .AddProvider<IAIProvider>("native-only", _ => custom.Object));

        // Assert
        act.Should().NotThrow("a Native provider supplied with its own IAIProvider is valid");
    }

    [Fact]
    public void Seeding_MessagesApi_UsesMessagesEndpointNotChatEndpoint()
    {
        // Arrange — a MessagesApi definition with the default ChatEndpoint ("chat/completions")
        // but an explicit MessagesEndpoint ("messages"). The seeding must NOT overwrite
        // MessagesEndpoint with the ChatEndpoint default.
        var services = new ServiceCollection();
        var definition = new ProviderDefinition
        {
            Id = "anthropic-test",
            DisplayName = "Anthropic Test",
            Protocol = EProviderProtocol.MessagesApi,
            BaseUrl = "https://api.test.com/v1/",
            MessagesEndpoint = "messages",
        };

        // Act
        services.AddAiProviders(new[] { definition });
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsFactory<MessagesApiOptions>>()
            .Create("anthropic-test");

        // Assert
        options.MessagesEndpoint.Should().Be("messages",
            "MessagesEndpoint is seeded from definition.MessagesEndpoint, not ChatEndpoint");
        options.ModelsEndpoint.Should().Be("models");
    }

    [Fact]
    public void Seeding_CopiesProtocolConfiguration_FromDefinition()
    {
        // Arrange
        var services = new ServiceCollection();
        var definition = new ProviderDefinition
        {
            Id = "test-messages",
            DisplayName = "Test Messages",
            Protocol = EProviderProtocol.MessagesApi,
            BaseUrl = "https://test.example.com/v1/",
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["anthropicVersion"] = "2023-06-01",
                ["apiKeyHeaderName"] = "x-api-key"
            }
        };

        // Act
        services.AddAiProviders(new[] { definition });
        using var provider = services.BuildServiceProvider();

        var options = provider
            .GetRequiredService<IOptionsFactory<MessagesApiOptions>>()
            .Create("test-messages");

        // Assert
        options.ProtocolConfiguration.Should().NotBeNull();
        options.ProtocolConfiguration.Should().ContainKey("anthropicVersion");
        options.ProtocolConfiguration.Should().ContainKey("apiKeyHeaderName");
    }

    [Fact]
    public void Seeding_KeyQueryOptions_CopiesEndpointPatternsFromProtocolConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        var definition = new ProviderDefinition
        {
            Id = "test-keyquery",
            DisplayName = "Test KeyQuery",
            Protocol = EProviderProtocol.KeyQuery,
            BaseUrl = "https://test.example.com/v1/",
            ModelsEndpoint = "v1/custom-models",
            ProtocolConfiguration = new Dictionary<string, string>
            {
                ["apiKeyHeaderName"] = "x-goog-api-key",
                ["generationEndpoint"] = "v1/{model}:customAction",
                ["streamEndpoint"] = "v1/{model}:customStream"
            }
        };

        // Act
        services.AddAiProviders(new[] { definition });
        using var sp = services.BuildServiceProvider();

        var options = sp
            .GetRequiredService<IOptionsFactory<KeyQueryOptions>>()
            .Create("test-keyquery");

        // Assert — endpoint patterns come from protocolConfiguration
        options.ChatEndpoint.Should().Be("v1/{model}:customAction");
        options.StreamEndpoint.Should().Be("v1/{model}:customStream");
        options.CustomAuthHeaderName.Should().Be("x-goog-api-key");
        // ModelsEndpoint still comes from the generic definition field
        options.ModelsEndpoint.Should().Be("v1/custom-models");
    }

    [Fact]
    public void Endpoints_DecisionsProtocol_RegistersCombinedProviderUnderOneId()
    {
        // Arrange — a single OpenAICompatible definition with an endpoints["decisions"] entry
        // declaring protocol: "decision". One id must replace what was previously two registrations
        // (one chat, one decisions) — the core value proposition of feature 16.
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        var definition = new ProviderDefinition
        {
            Id = "combined-provider",
            DisplayName = "Combined",
            BaseUrl = "https://test.example.com/api/v1/",
            Protocol = EProviderProtocol.OpenAICompatible,
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["decisions"] = new EndpointDefinition
                {
                    Path = "alpha/decisions",
                    Protocol = EProviderProtocol.Decision
                }
            }
        };

        // Act
        services.AddAiProviders(new[] { definition });
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("combined-provider");

        // Assert — one id serves both families
        resolved.Should().BeOfType<OpenAICompatibleDecisionProvider>();
        resolved.Should().BeAssignableTo<IStreamingChatProvider>();
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        ((IDecisionProvider)resolved).SupportsDecisions.Should().BeTrue();
    }

    [Fact]
    public void ConsumerConfigure_WinsOverLegacyFlatFieldAndEndpointsEntry()
    {
        // Arrange — a definition with both a legacy flat ChatEndpoint and an endpoints["chat"] entry.
        // The consumer's Configure<> callback must win over both.
        var services = new ServiceCollection();
        var definition = new ProviderDefinition
        {
            Id = "override-test",
            DisplayName = "Override Test",
            BaseUrl = "https://test.example.com/api/v1/",
            Protocol = EProviderProtocol.OpenAICompatible,
            ChatEndpoint = "legacy/chat",  // legacy flat field
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["chat"] = new EndpointDefinition { Path = "endpoints/chat" }  // endpoints entry
            }
        };

        // Act
        services.AddAiProviders(
            b => b
                .Add(definition)
                .Configure<OpenAICompatibleProviderOptions>(
                    "override-test",
                    o => o.ChatEndpoint = "consumer/chat"));
        using var sp = services.BuildServiceProvider();

        var options = sp
            .GetRequiredService<IOptionsFactory<OpenAICompatibleProviderOptions>>()
            .Create("override-test");

        // Assert — consumer Configure<> runs after seeding and wins over both sources
        options.ChatEndpoint.Should().Be("consumer/chat",
            "consumer Configure<> wins over both the legacy flat field and the endpoints entry");
    }
}
