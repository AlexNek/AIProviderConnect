using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// Tests for <c>IAIProviderFactory.GetProvider(string, RequestCredentials)</c> via <c>DefaultAIProviderFactory</c>.
/// </summary>
public class DefaultAIProviderFactoryRequestCredentialsTests
{
    private const string ChatJson =
        """{"choices":[{"message":{"content":"hi"},"finish_reason":"stop"}]}""";
    private const string EmbeddingsJson = """{"data":[{"index":0,"embedding":[0.1]}]}""";

    private static ProviderDefinition Definition(
        string id = "test-provider",
        EProviderProtocol protocol = EProviderProtocol.OpenAICompatible) =>
        new()
        {
            Id = id,
            DisplayName = "Test provider",
            BaseUrl = "https://test.example.com/v1/",
            Protocol = protocol
        };

    private static IAIProviderFactory BuildFactory(
        CapturingHttpMessageHandler handler,
        Action<AIProviderRegistrationBuilder>? configureBuilder = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new HttpClient(handler));
        services.AddLogging();
        services.AddAiProviders(b =>
        {
            b.Add(Definition())
                .Configure<OpenAICompatibleProviderOptions>("test-provider", o =>
                {
                    o.ApiKey = "fake-api-key";
                    o.DefaultModel = "test-model";
                });
            configureBuilder?.Invoke(b);
        });
        return services.BuildServiceProvider().GetRequiredService<IAIProviderFactory>();
    }

    private static ChatCompletionRequest ChatRequest(string model = "request-model") => new()
    {
        Model = model,
        Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
    };

    private static string CapturedModel(CapturingHttpMessageHandler handler)
    {
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        return doc.RootElement.GetProperty("model").GetString()!;
    }

    [Fact]
    public async Task OverrideApiKey_AppliedToAuthorizationHeader()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var factory = BuildFactory(handler);
        var creds = new RequestCredentials { ApiKey = "override-key" };

        // Act
        var provider = factory.GetProvider("test-provider", creds);
        await provider.ChatAsync(ChatRequest());

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("override-key");
    }

    [Fact]
    public async Task OverrideBaseUrl_ChangesRequestHost()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var factory = BuildFactory(handler);
        var creds = new RequestCredentials { BaseUrl = "https://override.example.com/v1/" };

        // Act
        var provider = factory.GetProvider("test-provider", creds);
        await provider.ChatAsync(ChatRequest());

        // Assert
        handler.LastRequest!.RequestUri!.Host.Should().Be("override.example.com");
    }

    [Fact]
    public async Task OverrideModel_AppliedEvenWhenRequestCarriesModel()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var factory = BuildFactory(handler);
        var creds = new RequestCredentials { Model = "override-model" };

        // Act
        var provider = factory.GetProvider("test-provider", creds);
        await provider.ChatAsync(ChatRequest("request-model"));

        // Assert
        CapturedModel(handler).Should().Be("override-model");
    }

    [Fact]
    public async Task EmbeddingProviderHonorsApiKeyAndModelOverrides()
    {
        // Arrange — use a handler that returns the embeddings response.
        using var handler = new CapturingHttpMessageHandler(EmbeddingsJson);
        var factory = BuildFactory(handler);
        var creds = new RequestCredentials { ApiKey = "override-key", Model = "embed-override" };

        // Act
        var provider = factory.GetProvider("test-provider", creds);
        var embeddingProvider = provider as IEmbeddingProvider;
        embeddingProvider.Should().NotBeNull("transient OpenAICompatibleProvider implements IEmbeddingProvider");
        await embeddingProvider!.EmbedAsync(new EmbeddingRequest { Input = ["hello"] });

        // Assert
        handler.LastRequest!.Headers.Authorization!.Parameter.Should().Be("override-key");
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("embed-override");
    }

    [Fact]
    public void TransientInstances_AreDifferentFromSingleton()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var factory = BuildFactory(handler);
        var creds = new RequestCredentials { ApiKey = "override-key" };

        // Act
        var singleton = factory.GetProvider("test-provider");
        var transient1 = factory.GetProvider("test-provider", creds);
        var transient2 = factory.GetProvider("test-provider", creds);

        // Assert
        transient1.Should().NotBeSameAs(singleton);
        transient2.Should().NotBeSameAs(singleton);
        transient1.Should().NotBeSameAs(transient2);
    }

    [Fact]
    public void SingletonInstance_StableAcrossCalls()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var factory = BuildFactory(handler);

        // Act
        var first = factory.GetProvider("test-provider");
        var second = factory.GetProvider("test-provider");

        // Assert
        first.Should().BeSameAs(second);
    }

    [Fact]
    public void NonAIProviderBase_ThrowsConfigurationError()
    {
        // Arrange — register a custom provider that does not derive from AIProviderBase.
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var factory = BuildFactory(handler, b =>
        {
            b.Add(Definition("custom-plain"));
            b.AddProvider<SimpleNonAIProviderBase>(
                "custom-plain", _ => new SimpleNonAIProviderBase());
        });

        // Act
        Action act = () => factory.GetProvider(
            "custom-plain", new RequestCredentials { ApiKey = "override-key" });

        // Assert
        var exception = act.Should().Throw<AiException>().Which;
        exception.Code.Should().Be(AiErrorCodes.ConfigurationError);
    }

    [Fact]
    public async Task ConcurrentCalls_CarryDistinctAuthorizationHeaders()
    {
        // Arrange — two DI containers each with its own handler to capture independently.
        using var handlerA = new CapturingHttpMessageHandler(ChatJson);
        using var handlerB = new CapturingHttpMessageHandler(ChatJson);
        var factoryA = BuildFactory(handlerA);
        var factoryB = BuildFactory(handlerB);

        // Act — build two transient providers with different keys and invoke simultaneously.
        var providerA = factoryA.GetProvider(
            "test-provider", new RequestCredentials { ApiKey = "key-aaa" });
        var providerB = factoryB.GetProvider(
            "test-provider", new RequestCredentials { ApiKey = "key-bbb" });

        await Task.WhenAll(
            providerA.ChatAsync(ChatRequest()),
            providerB.ChatAsync(ChatRequest()));

        // Assert — no cross-contamination.
        handlerA.LastRequest!.Headers.Authorization!.Parameter.Should().Be("key-aaa");
        handlerB.LastRequest!.Headers.Authorization!.Parameter.Should().Be("key-bbb");
    }
}
