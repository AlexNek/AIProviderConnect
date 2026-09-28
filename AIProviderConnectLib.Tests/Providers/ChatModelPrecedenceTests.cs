using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace AIProviderConnectLib.Tests.Providers;

public class ChatModelPrecedenceTests
{
    private const string ChatJson = """{"choices":[{"message":{"content":"hi"},"finish_reason":"stop"}]}""";
    private const string EmbeddingsJson = """{"data":[{"index":0,"embedding":[0.1]}]}""";

    private static ProviderCatalog Catalog() => new([
        new ProviderDefinition
        {
            Id = "test-provider", DisplayName = "Test provider",
            BaseUrl = "https://test.example.com/v1/", Protocol = EProviderProtocol.OpenAICompatible
        }
    ]);

    private static OpenAICompatibleProvider CreateProvider(
        CapturingHttpMessageHandler handler,
        ICredentialResolver? resolver = null,
        string responseJson = ChatJson,
        Action<OpenAICompatibleProviderOptions>? configureOptions = null)
    {
        var options = new OpenAICompatibleProviderOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "fake-api-key",
            Enabled = true
        };
        configureOptions?.Invoke(options);
        return new OpenAICompatibleProvider(
            new HttpClient(handler), options, Catalog(), "test-provider",
            NullLogger.Instance, resolver);
    }

    private static ICredentialResolver ResolverReturning(RequestCredentials? credentials)
    {
        var mock = new Moq.Mock<ICredentialResolver>();
        mock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<RequestCredentials?>(credentials));
        return mock.Object;
    }

    private static ChatCompletionRequest SampleRequest(string model) => new()
    {
        Model = model,
        Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
    };

    private static string CapturedModel(CapturingHttpMessageHandler handler)
    {
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        return doc.RootElement.GetProperty("model").GetString()!;
    }

    [Theory]
    [InlineData("override-model", "test-model", "override-model")]
    [InlineData("test-model", "test-model", "  ")]
    [InlineData("default-model", "  ", null)]
    public async Task ChatModel_PrecedenceOverrideRequestDefault(string expected, string requestModel, string? credentialsModel)
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var credentials = credentialsModel is null
            ? null
            : new RequestCredentials { Model = credentialsModel };
        var provider = CreateProvider(
            handler, ResolverReturning(credentials),
            configureOptions: o => o.DefaultModel = "default-model");

        // Act
        await provider.ChatAsync(SampleRequest(requestModel));

        // Assert
        CapturedModel(handler).Should().Be(expected);
    }

    [Fact]
    public async Task EmptyEffectiveChatModel_ThrowsInvalidRequest()
    {
        // Arrange — request whitespace, no default, no override.
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = () => provider.ChatAsync(SampleRequest("   "));

        // Assert
        (await act.Should().ThrowAsync<AiException>()).Which.Code.Should().Be(AiErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task ConfigurationGate_PrecedesEmptyModelResolution()
    {
        // Arrange — a disabled provider and a request with no model: the actionable configuration
        // error must surface instead of ai/invalid-request, as the streaming docs already promise.
        using var handler = new CapturingHttpMessageHandler(ChatJson);
        var provider = CreateProvider(handler, configureOptions: o => o.Enabled = false);

        // Act
        Func<Task> act = () => provider.ChatAsync(SampleRequest("   "));

        // Assert
        (await act.Should().ThrowAsync<AiException>()).Which.Code.Should().Be(AiErrorCodes.ProviderDisabled);
    }

    [Fact]
    public async Task Embedding_ChatDefaultModelIsNeverUsed()
    {
        // Arrange — chat DefaultModel set, embedding default empty, request model empty, no override.
        using var handler = new CapturingHttpMessageHandler(EmbeddingsJson);
        var provider = CreateProvider(
            handler, responseJson: EmbeddingsJson, configureOptions: o => o.DefaultModel = "chat-model");

        // Act
        Func<Task> act = () => provider.EmbedAsync(new EmbeddingRequest { Input = ["hello"] });

        // Assert
        (await act.Should().ThrowAsync<AiException>())
            .Which.Code.Should().Be(AiErrorCodes.EmbeddingModelNotConfigured);
    }

    [Fact]
    public async Task Embedding_CredentialsModelBeatsRequestModel()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(EmbeddingsJson);
        var provider = CreateProvider(
            handler,
            ResolverReturning(new RequestCredentials { Model = "embed-override" }),
            EmbeddingsJson);

        // Act
        await provider.EmbedAsync(new EmbeddingRequest { Model = "embed-model", Input = ["hello"] });

        // Assert
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        doc.RootElement.GetProperty("model").GetString().Should().Be("embed-override");
    }
}
