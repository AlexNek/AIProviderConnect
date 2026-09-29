using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace AIProviderConnectLib.Tests.Providers;

public class KeyQueryModelInUrlTests
{
    // A Key Query (Gemini-style) response: the wire format carries the model in the URL, not the body,
    // and ParseResponse echoes the model it was handed back on the response object.
    private const string KeyQueryJson =
        """{"candidates":[{"content":{"parts":[{"text":"hi"}]}}]}""";

    private static ProviderCatalog Catalog() => new([
        new ProviderDefinition
        {
            Id = "test-kq", DisplayName = "Test key query",
            BaseUrl = "https://test.example.com/v1/", Protocol = EProviderProtocol.KeyQuery
        }
    ]);

    private static ICredentialResolver ResolverReturningModel(string model)
    {
        var mock = new Mock<ICredentialResolver>();
        mock
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<RequestCredentials?>(new RequestCredentials { Model = model }));
        return mock.Object;
    }

    private static KeyQueryProvider CreateProvider(
        CapturingHttpMessageHandler handler,
        ICredentialResolver? resolver)
    {
        var options = new KeyQueryOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "fake-api-key",
            CustomAuthHeaderName = "x-test-key",
            ChatEndpoint = "models/{model}:generateContent",
            Enabled = true
        };
        return new KeyQueryProvider(
            new HttpClient(handler), options, Catalog(), "test-kq",
            NullLogger.Instance, resolver);
    }

    [Fact]
    public async Task CredentialsModel_NamesUrlAndParser_EvenWhenRequestCarriesModel()
    {
        // Arrange — request names one model, the override names another.
        using var handler = new CapturingHttpMessageHandler(KeyQueryJson);
        var provider = CreateProvider(handler, ResolverReturningModel("override-model"));
        var request = new ChatCompletionRequest
        {
            Model = "request-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        };

        // Act
        var response = await provider.ChatAsync(request);

        // Assert — the {model} endpoint template resolved to the override.
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("override-model");
        handler.LastRequest!.RequestUri!.ToString().Should().NotContain("request-model");

        // Assert — the model handed to the parser (and echoed on the response) is the override.
        response.Model.Should().Be("override-model");
    }

    [Fact]
    public async Task NoOverride_UrlUsesRequestModel()
    {
        // Arrange — no resolver, so the request model flows through unchanged.
        using var handler = new CapturingHttpMessageHandler(KeyQueryJson);
        var provider = CreateProvider(handler, resolver: null);
        var request = new ChatCompletionRequest
        {
            Model = "request-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        };

        // Act
        var response = await provider.ChatAsync(request);

        // Assert
        handler.LastRequest!.RequestUri!.ToString().Should().Contain("request-model");
        response.Model.Should().Be("request-model");
    }
}
