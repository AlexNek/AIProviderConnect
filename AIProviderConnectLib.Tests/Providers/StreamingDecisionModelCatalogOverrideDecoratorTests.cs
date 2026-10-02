using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// Proves the model-override decorator chain stays capability-transparent for the
/// streaming-and-decisions combination: an override on a streaming composite must never hide
/// <c>is IDecisionProvider</c> (rule 15), mirroring DecisionProviderRegistrationTests.
/// </summary>
public class StreamingDecisionModelCatalogOverrideDecoratorTests
{
    private static ProviderDefinition Definition() => new()
    {
        Id = "streaming-decisions",
        DisplayName = "Streaming decisions provider",
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

    [Fact]
    public void StreamingAndDecisionsProvider_WithOverrides_AnswersBothCapabilities()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b
            .Add(Definition())
            .OverrideModels("streaming-decisions",
                [new ModelOverride { Id = "m1", Capabilities = EModelCapability.Decision }]));

        // Act
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("streaming-decisions");

        // Assert — the decorator must be the streaming-and-decisions one, not the
        // streaming-only ModelCatalogOverrideDecorator that would hide IDecisionProvider.
        resolved.Should().BeAssignableTo<IStreamingChatProvider>();
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        ((IDecisionProvider)resolved).SupportsDecisions.Should().BeTrue();
        resolved.Should().BeOfType<StreamingDecisionModelCatalogOverrideDecorator>();
    }

    [Fact]
    public void StreamingOnlyProvider_WithOverrides_UsesStreamingDecorator()
    {
        // Arrange — no decisions override: the existing streaming decorator serves the id
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b
            .Add(new ProviderDefinition
            {
                Id = "streaming-only",
                DisplayName = "Streaming only",
                BaseUrl = "https://test.example.com/api/v1/",
                Protocol = EProviderProtocol.OpenAICompatible
            })
            .OverrideModels("streaming-only",
                [new ModelOverride { Id = "m1", Capabilities = EModelCapability.TextGeneration }]));

        // Act
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("streaming-only");

        // Assert
        resolved.Should().BeAssignableTo<IStreamingChatProvider>();
        resolved.Should().NotBeAssignableTo<IDecisionProvider>();
        resolved.Should().BeOfType<ModelCatalogOverrideDecorator>();
    }

    [Fact]
    public void Decorator_ForwardsIdAndMergesOverrides()
    {
        // Arrange
        var store = new InMemoryModelOverrideStore();
        store.Add("streaming-decisions", [new ModelOverride { Id = "m1", Capabilities = EModelCapability.Decision }]);
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b.Add(Definition()));
        using var provider = services.BuildServiceProvider();
        var inner = provider.GetRequiredKeyedService<IAIProvider>("streaming-decisions");
        var decorator = new StreamingDecisionModelCatalogOverrideDecorator(inner, store);

        // Assert
        decorator.Id.Should().Be(inner.Id);
        decorator.IsEnabled.Should().Be(inner.IsEnabled);
        decorator.Protocol.Should().Be(inner.Protocol);
        decorator.SupportsDecisions.Should().BeTrue();
        decorator.SupportsModelDiscovery.Should().BeTrue();
    }
}
