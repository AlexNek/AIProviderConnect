using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// Plan x1 (feature 14) mandatory coverage: every transport — chat, streaming, model discovery,
/// embeddings — must honor per-call credentials on both the <see cref="ICredentialResolver"/> path
/// and the <c>GetProvider(id, overrides)</c> factory path. The chat combinations live in
/// <c>CredentialResolverTests</c> and <c>DefaultAIProviderFactoryRequestCredentialsTests</c>; this
/// class covers the streaming, model-discovery, and embeddings combinations. Embeddings are exposed
/// only by the OpenAI-compatible family, so that transport is exercised against one provider type.
/// </summary>
public class CredentialTransportCoverageTests
{
    private const string OpenAiStream =
        """
        data: {"choices":[{"delta":{"content":"Hi"}}]}

        data: [DONE]
        """;
    private const string KeyQueryStream =
        """
        data: {"candidates":[{"content":{"parts":[{"text":"Hi"}]}}]}

        data: [DONE]
        """;
    private const string MessagesStream =
        """
        data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Hi"}}

        data: [DONE]
        """;
    private const string ModelsJson = """{"data":[{"id":"model-1","object":"model"}]}""";
    private const string EmbeddingsJson = """{"data":[{"index":0,"embedding":[0.1]}]}""";

    private static ProviderDefinition Definition(
        string id,
        EProviderProtocol protocol = EProviderProtocol.OpenAICompatible) =>
        new()
        {
            Id = id,
            DisplayName = "Test provider",
            BaseUrl = "https://test.example.com/v1/",
            Protocol = protocol
        };

    private static ProviderCatalog Catalog(params ProviderDefinition[] definitions) =>
        new(definitions.Length == 0 ? [Definition("test-provider")] : definitions);

    private static ICredentialResolver ResolverReturning(
        string? apiKey = null,
        string? baseUrl = null)
    {
        var mock = new Mock<ICredentialResolver>();
        mock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<RequestCredentials?>(new RequestCredentials
            {
                ApiKey = apiKey,
                BaseUrl = baseUrl
            }));
        return mock.Object;
    }

    private static OpenAICompatibleProviderOptions OpenAiOptions() => new()
    {
        BaseUrl = "https://test.example.com/v1/",
        ApiKey = "fake-api-key",
        Enabled = true,
        DefaultModel = "test-model"
    };

    private static KeyQueryOptions KeyQueryOptions() => new()
    {
        BaseUrl = "https://test.example.com/v1/",
        ApiKey = "fake-api-key",
        Enabled = true,
        DefaultModel = "test-model",
        CustomAuthHeaderName = "x-test-key",
        ChatEndpoint = "models/{model}:generateContent",
        StreamEndpoint = "models/{model}:streamGenerateContent"
    };

    private static MessagesApiOptions MessagesOptions() => new()
    {
        BaseUrl = "https://test.example.com/v1/",
        ApiKey = "fake-api-key",
        Enabled = true,
        DefaultModel = "test-model",
        CustomAuthHeaderName = "x-api-key",
        MessagesEndpoint = "v1/messages"
    };

    private static ChatCompletionRequest ChatRequest() => new()
    {
        Model = "test-model",
        Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
    };

    private static async Task DrainAsync(IAsyncEnumerable<StreamingChatChunk> chunks)
    {
        await foreach (var _ in chunks)
        {
        }
    }

    private static IAIProviderFactory BuildFactory(CapturingHttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler));
        services.AddLogging();
        services.AddAiProviders(b => b
            .Add(Definition("test-provider"))
            .Configure<OpenAICompatibleProviderOptions>("test-provider", o =>
            {
                o.ApiKey = "fake-api-key";
                o.DefaultModel = "test-model";
            }));
        return services.BuildServiceProvider().GetRequiredService<IAIProviderFactory>();
    }

    // --- Resolver path: streaming honors key + base URL override -------------------------------

    [Fact]
    public async Task OpenAiStreamAsync_ResolverOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(OpenAiStream);
        var provider = new OpenAICompatibleProvider(
            new HttpClient(handler), OpenAiOptions(), Catalog(), "test-provider",
            NullLogger.Instance, ResolverReturning("resolver-key", "https://override.example.com/v1/"));

        // Act
        await DrainAsync(provider.StreamAsync(ChatRequest()));

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("resolver-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task MessagesApiStreamAsync_ResolverOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(MessagesStream);
        var provider = new MessagesApiProvider(
            new HttpClient(handler), MessagesOptions(), Catalog(Definition("anthropic", EProviderProtocol.MessagesApi)),
            "anthropic", NullLogger.Instance,
            ResolverReturning("resolver-key", "https://override.example.com/v1/"));

        // Act
        await DrainAsync(provider.StreamAsync(ChatRequest()));

        // Assert — the custom API-key header carries the override, not the configured key.
        handler.LastRequest!.Headers.GetValues("x-api-key").Should().Contain("resolver-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task KeyQueryStreamAsync_ResolverOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(KeyQueryStream);
        var provider = new KeyQueryProvider(
            new HttpClient(handler), KeyQueryOptions(), Catalog(Definition("test-kq", EProviderProtocol.KeyQuery)),
            "test-kq", NullLogger.Instance,
            ResolverReturning("resolver-key", "https://override.example.com/v1/"));

        // Act
        await DrainAsync(provider.StreamAsync(ChatRequest()));

        // Assert
        handler.LastRequest!.Headers.GetValues("x-test-key").Should().Contain("resolver-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    // --- Resolver path: model discovery honors key + base URL override -------------------------

    [Fact]
    public async Task OpenAiGetModelsAsync_ResolverOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ModelsJson);
        var provider = new OpenAICompatibleProvider(
            new HttpClient(handler), OpenAiOptions(), Catalog(), "test-provider",
            NullLogger.Instance, ResolverReturning("resolver-key", "https://override.example.com/v1/"));

        // Act
        var models = await provider.GetModelsAsync();

        // Assert
        models.Should().ContainSingle();
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("resolver-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task MessagesApiGetModelsAsync_ResolverOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ModelsJson);
        var provider = new MessagesApiProvider(
            new HttpClient(handler), MessagesOptions(), Catalog(Definition("anthropic", EProviderProtocol.MessagesApi)),
            "anthropic", NullLogger.Instance,
            ResolverReturning("resolver-key", "https://override.example.com/v1/"));

        // Act
        await provider.GetModelsAsync();

        // Assert
        handler.LastRequest!.Headers.GetValues("x-api-key").Should().Contain("resolver-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task KeyQueryGetModelsAsync_ResolverOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ModelsJson);
        var provider = new KeyQueryProvider(
            new HttpClient(handler), KeyQueryOptions(), Catalog(Definition("test-kq", EProviderProtocol.KeyQuery)),
            "test-kq", NullLogger.Instance,
            ResolverReturning("resolver-key", "https://override.example.com/v1/"));

        // Act
        await provider.GetModelsAsync();

        // Assert
        handler.LastRequest!.Headers.GetValues("x-test-key").Should().Contain("resolver-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    // --- Factory-overload path: streaming + discovery honor FixedCredentials --------------------

    [Fact]
    public async Task OpenAiStreamAsync_FactoryOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(OpenAiStream);
        var factory = BuildFactory(handler);

        // Act
        var provider = factory.GetProvider("test-provider", new RequestCredentials
        {
            ApiKey = "override-key",
            BaseUrl = "https://override.example.com/v1/"
        }) as IStreamingChatProvider;
        provider.Should().NotBeNull("transient OpenAICompatibleProvider exposes streaming");
        await DrainAsync(provider!.StreamAsync(ChatRequest()));

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("override-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task OpenAiGetModelsAsync_FactoryOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ModelsJson);
        var factory = BuildFactory(handler);

        // Act
        var provider = factory.GetProvider("test-provider", new RequestCredentials
        {
            ApiKey = "override-key",
            BaseUrl = "https://override.example.com/v1/"
        }) as IModelDiscoveryProvider;
        provider.Should().NotBeNull("transient OpenAICompatibleProvider exposes model discovery");
        await provider!.GetModelsAsync();

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("override-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    // --- Embeddings honor key + base URL overrides on both paths --------------------------------

    [Fact]
    public async Task OpenAiEmbedAsync_ResolverOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(EmbeddingsJson);
        var provider = new OpenAICompatibleProvider(
            new HttpClient(handler), OpenAiOptions(), Catalog(), "test-provider",
            NullLogger.Instance, ResolverReturning("resolver-key", "https://override.example.com/v1/"));

        // Act
        await provider.EmbedAsync(new EmbeddingRequest { Model = "embed-model", Input = ["hello"] });

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("resolver-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task OpenAiEmbedAsync_FactoryOverridesKeyAndBaseUrl()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(EmbeddingsJson);
        var factory = BuildFactory(handler);

        // Act
        var provider = factory.GetProvider("test-provider", new RequestCredentials
        {
            ApiKey = "override-key",
            BaseUrl = "https://override.example.com/v1/"
        }) as IEmbeddingProvider;
        provider.Should().NotBeNull("transient OpenAICompatibleProvider exposes embeddings");
        await provider!.EmbedAsync(new EmbeddingRequest { Model = "embed-model", Input = ["hello"] });

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("override-key");
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }
}
