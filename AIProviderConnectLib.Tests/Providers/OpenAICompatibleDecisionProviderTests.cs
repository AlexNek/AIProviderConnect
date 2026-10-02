using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Constants;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// Drives the combined chat/decisions class against the handler fakes, asserting request-URL
/// composition (override base plus path, common base when no override), capability discovery,
/// and response parsing — all hermetic.
/// </summary>
public class OpenAICompatibleDecisionProviderTests
{
    private const string DecisionResponseJson = """
    {
        "id": "resp-1",
        "model": "typesafe/jev-1.13",
        "provider": "TypeSafe",
        "answers": {
            "priority": { "type": "noul", "noul": 0.7 }
        },
        "usage": { "input_tokens": 10, "output_tokens": 4 }
    }
    """;

    private const string ChatResponseJson = """
    { "id": "c1", "model": "m1", "choices": [ { "message": { "role": "assistant", "content": "hi" } } ] }
    """;

    private const string ModelsResponseJson = """
    { "data": [ { "id": "m1" }, { "id": "m2" } ] }
    """;

    private const string EmbeddingsResponseJson = """
    { "data": [ { "index": 0, "embedding": [0.1, 0.2] } ], "model": "e1" }
    """;

    private static DecisionRequest DecisionRequestSample(string model = "typesafe/jev-1.13") => new()
    {
        Model = model,
        State = "A ticket about a billing error.",
        Questions = new Dictionary<string, DecisionQuestion>
        {
            ["priority"] = new() { Kind = EDecisionQuestionKind.Noul }
        }
    };

    private static OpenAICompatibleProviderOptions Options(
        string baseUrl = "https://test.example.com/api/v1/",
        string? decisionsBaseUrl = null,
        string defaultModel = "typesafe/jev-1.13") =>
        new()
        {
            BaseUrl = baseUrl,
            ApiKey = "fake-api-key",
            Enabled = true,
            DefaultModel = defaultModel,
            DecisionsEndpoint = "alpha/decisions",
            DecisionsBaseUrl = decisionsBaseUrl
        };

    private static ProviderCatalog Catalog() => new(
    [
        new ProviderDefinition
        {
            Id = "combined",
            DisplayName = "Combined provider",
            BaseUrl = "https://test.example.com/api/v1/",
            Protocol = EProviderProtocol.OpenAICompatible,
            HasModelDiscoveryApi = true
        }
    ]);

    private static OpenAICompatibleDecisionProvider Create(
        CapturingHttpMessageHandler handler,
        OpenAICompatibleProviderOptions? options = null) =>
        new(new HttpClient(handler), options ?? Options(), Catalog(), "combined");

    [Fact]
    public async Task DecideAsync_WithBaseUrlOverride_BuildsRequestFromOverridePlusPath()
    {
        // Arrange — the surface sits on /api/, not under the /api/v1/ common base
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = Create(handler, Options(decisionsBaseUrl: "https://test.example.com/api/"));

        // Act
        var response = await provider.DecideAsync(DecisionRequestSample());

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/api/alpha/decisions");
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization!.Parameter.Should().Be("fake-api-key");
        response.Answers["priority"].Noul!.ProbabilityOfYes.Should().Be(0.7);
    }

    [Fact]
    public async Task DecideAsync_WithoutOverride_ResolvesAgainstCommonBase()
    {
        // Arrange — no baseUrl override: the common base is the default
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = Create(handler, Options(decisionsBaseUrl: null));

        // Act
        await provider.DecideAsync(DecisionRequestSample());

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/api/v1/alpha/decisions");
    }

    [Fact]
    public async Task DecideAsync_NonAbsoluteOverride_ThrowsInvalidRequest()
    {
        // Arrange — a scheme-less decisions override must be rejected as AiException before
        // request build, mirroring the embeddings surface
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = Create(handler, Options(decisionsBaseUrl: "decisions.example.com/"));

        // Act
        Func<Task> act = () => provider.DecideAsync(DecisionRequestSample());

        // Assert
        var ex = (await act.Should().ThrowAsync<AIProviderConnect.Exceptions.AiException>()).Which;
        ex.Code.Should().Be(AIProviderConnect.Exceptions.AiErrorCodes.InvalidRequest);
        ex.Message.Should().Contain("is not a valid absolute URL");
    }

    [Fact]
    public async Task ChatAsync_InheritedTransport_UsesChatEndpointUnderCommonBase()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatResponseJson);
        var provider = Create(handler);

        // Act
        var response = await provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "m1",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "hi" }]
        });

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/api/v1/chat/completions");
        response.Content.Should().Be("hi");
    }

    [Fact]
    public async Task StreamAsync_IsStreamingCapable()
    {
        // Arrange — streaming comes from the base unchanged; assert the capability surface
        using var handler = new CapturingHttpMessageHandler(ChatResponseJson);
        var provider = Create(handler);

        // Assert
        provider.Should().BeAssignableTo<IStreamingChatProvider>();
    }

    [Fact]
    public async Task EmbedAsync_InheritedTransport_UsesEmbeddingsEndpoint()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(EmbeddingsResponseJson);
        var provider = Create(handler);

        // Act
        var response = await provider.EmbedAsync(new EmbeddingRequest
        {
            Model = "e1",
            Input = ["text"]
        });

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/api/v1/embeddings");
        response.Data.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetModelsAsync_InheritedTransport_UsesModelsEndpoint()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ModelsResponseJson);
        var provider = Create(handler);

        // Act
        var models = await provider.GetModelsAsync();

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .Should().Be("https://test.example.com/api/v1/models");
        models.Select(m => m.Id).Should().Equal("m1", "m2");
    }

    [Fact]
    public async Task DecideAsync_NoModelAnywhere_ThrowsInvalidRequest()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var provider = Create(handler, Options(defaultModel: string.Empty));

        // Act
        var act = () => provider.DecideAsync(DecisionRequestSample(model: string.Empty));

        // Assert
        (await act.Should().ThrowAsync<AIProviderConnect.Exceptions.AiException>())
            .Which.Code.Should().Be(AIProviderConnect.Exceptions.AiErrorCodes.InvalidRequest);
    }

    [Fact]
    public void Constructor_NonEndpointOptions_ThrowsArgument()
    {
        // Arrange — ChatOnlyOptions passes the base class guard (IChatAndModelsEndpointOptions)
        // but fails the derived class guard (IDecisionsEndpointOptions).
        using var handler = new CapturingHttpMessageHandler(DecisionResponseJson);
        var plainOptions = new ChatOnlyOptions
        {
            BaseUrl = "https://test.example.com/api/v1/"
        };

        // Act
        var act = () => new OpenAICompatibleDecisionProvider(
            new HttpClient(handler), plainOptions, Catalog(), "combined");

        // Assert
        act.Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain("IDecisionsEndpointOptions");
    }

    /// <summary>
    /// Test-only options that pass the base class guards
    /// (<c>IChatAndModelsEndpointOptions</c>, <c>IEmbeddingsEndpointOptions</c>) but not
    /// the derived class guard (<c>IDecisionsEndpointOptions</c>).
    /// </summary>
    private sealed class ChatOnlyOptions : AIProviderOptions, IChatAndModelsEndpointOptions, IEmbeddingsEndpointOptions
    {
        public string ChatEndpoint { get; set; } = EndpointDefaults.ChatCompletions;
        public string ModelsEndpoint { get; set; } = EndpointDefaults.Models;
        public string EmbeddingsEndpoint { get; set; } = EndpointDefaults.Embeddings;
        public string DefaultEmbeddingModel { get; set; } = string.Empty;
        public string? EmbeddingsBaseUrl { get; set; }
    }

    [Fact]
    public async Task RegisteredUnderOneId_AnswersAllCapabilities()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton(new HttpClient());
        services.AddAiProviders(new[]
        {
            new ProviderDefinition
            {
                Id = "combined-id",
                DisplayName = "Combined",
                BaseUrl = "https://test.example.com/api/v1/",
                Protocol = EProviderProtocol.OpenAICompatible,
                Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
                {
                    ["decisions"] = new EndpointDefinition
                    {
                        Path = "alpha/decisions",
                        BaseUrl = "https://test.example.com/api/",
                        Protocol = EProviderProtocol.Decision
                    }
                }
            }
        });
        using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetRequiredKeyedService<IAIProvider>("combined-id");

        // Assert
        resolved.Should().BeAssignableTo<IStreamingChatProvider>();
        resolved.Should().BeAssignableTo<IModelDiscoveryProvider>();
        resolved.Should().BeAssignableTo<IEmbeddingProvider>();
        resolved.Should().BeAssignableTo<IDecisionProvider>();
        ((IDecisionProvider)resolved).SupportsDecisions.Should().BeTrue();
        await Task.CompletedTask;
    }
}
