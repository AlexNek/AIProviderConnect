using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// The model-override decorators sit between the consumer and the inner provider, so the two
/// default-implemented capability members would silently fall back if they were not forwarded: a
/// decorated read-out provider must still report <see cref="EDecisionSource.Readout"/> and the inner's
/// <see cref="IAIProvider.RequiresApiKey"/>, a decorated non-embedding streaming provider must
/// report decisions without claiming an embeddings surface it does not have, and a decorated
/// non-streaming decision provider keeps exactly the capabilities its inner has — no more, no less.
/// </summary>
public class ModelOverrideDecoratorCapabilityTests
{
    private static ProviderDefinition Definition(string id, EProviderProtocol protocol) => new()
    {
        Id = id,
        DisplayName = "Decorated provider",
        BaseUrl = "https://test.example.com/v1/",
        Protocol = protocol
    };

    private static ServiceProvider Build(ProviderDefinition definition)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b
            .Add(definition)
            .OverrideModels(definition.Id,
                [new ModelOverride { Id = "m1", Capabilities = EModelCapability.TextGeneration }]));

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ReadoutProviderWithOverrides_ReportsReadoutAndForwardsRequiresApiKey()
    {
        // Arrange — the definition pins requiresApiKey false, which must survive the decorator
        var definition = Definition("readout-decorated", EProviderProtocol.OpenAICompatible)
            with { RequiresApiKey = false };

        // Act
        using var provider = Build(definition);
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(definition.Id);

        // Assert
        resolved.Should().BeOfType<StreamingDecisionModelCatalogOverrideDecorator>();
        var decision = (IDecisionProvider)resolved;
        decision.SupportsDecisions.Should().BeTrue();
        decision.DecisionSource.Should().Be(EDecisionSource.Readout);
        resolved.RequiresApiKey.Should().BeFalse();
    }

    [Theory]
    [InlineData(EProviderProtocol.MessagesApi)]
    [InlineData(EProviderProtocol.KeyQuery)]
    [InlineData(EProviderProtocol.Catalog)]
    public void NonEmbeddingStreamingProviders_WithOverrides_StayDecisionCapable(
        EProviderProtocol protocol)
    {
        // Arrange
        var definition = Definition($"decorated-{protocol}", protocol);

        // Act
        using var provider = Build(definition);
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(definition.Id);

        // Assert — the non-embedding decorator forwards streaming and decisions but never embeds
        resolved.Should().BeOfType<StreamingDecisionNonEmbeddingModelCatalogOverrideDecorator>();
        resolved.Should().BeAssignableTo<IStreamingChatProvider>();
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        resolved.Should().NotBeAssignableTo<IEmbeddingProvider>();
        ((IDecisionProvider)resolved).DecisionSource.Should().Be(EDecisionSource.Readout);
    }

    [Fact]
    public void DecisionProtocolProvider_WithOverrides_StaysNonEmbedding()
    {
        // Arrange — the Decision root is the library's built-in non-streaming decision provider; it
        // has no embeddings surface, so the non-embedding decorator must stay selected for it
        var definition = Definition("decorated-decision", EProviderProtocol.Decision);

        // Act
        using var provider = Build(definition);
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(definition.Id);

        // Assert
        resolved.Should().BeOfType<DecisionModelCatalogOverrideDecorator>();
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        resolved.Should().NotBeAssignableTo<IEmbeddingProvider>();
        resolved.Should().NotBeAssignableTo<IStreamingChatProvider>();
    }

    [Fact]
    public async Task AuthoredEmbeddingDecisionProvider_WithOverrides_KeepsEmbeddingCapability()
    {
        // Arrange — an authored provider derives AIProviderBase (decision-capable, non-streaming) and
        // adds IEmbeddingProvider; overrides must not strip the embeddings capability
        var definition = Definition("authored-embedding-decision", EProviderProtocol.Native);
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(b => b
            .Add(definition)
            .AddProvider<AuthoredEmbeddingDecisionProvider>(definition.Id, sp =>
                new AuthoredEmbeddingDecisionProvider(
                    sp.GetRequiredService<HttpClient>(),
                    sp.GetRequiredService<IProviderCatalog>(),
                    new OpenAICompatibleProviderOptions(),
                    definition.Id))
            .OverrideModels(definition.Id,
                [new ModelOverride { Id = "m1", Capabilities = EModelCapability.TextGeneration }]));
        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetRequiredKeyedService<IAIProvider>(definition.Id);
        var embedding = (IEmbeddingProvider)resolved;
        var response = await embedding.EmbedAsync(
            new EmbeddingRequest { Model = "embed-model", Input = ["hello"] });

        // Assert — the embedding-capable decision decorator forwards EmbedAsync to the inner
        resolved.Should().BeOfType<DecisionEmbeddingModelCatalogOverrideDecorator>();
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        resolved.Should().BeAssignableTo<IEmbeddingProvider>();
        resolved.Should().NotBeAssignableTo<IStreamingChatProvider>();
        response.Data.Should().HaveCount(1);
        response.Model.Should().Be("embed-model");
    }
}
