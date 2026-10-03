using System.Net;
using System.Net.Http;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services;

namespace ScraperTool.Tests;

public class AIProviderFactoryTests
{
    private const string ChatResponseJson = """
        {"id":"chatcmpl-1","choices":[{"message":{"role":"assistant","content":"Hi"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}
        """;

    private readonly Mock<IProviderCatalog> _catalogMock;
    private readonly AppSettings _settings;
    private readonly CapturingHttpHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

    public AIProviderFactoryTests()
    {
        _catalogMock = new Mock<IProviderCatalog>();
        _catalogMock.Setup(c => c.Get("openai")).Returns(new ProviderDefinition
        {
            Id = "openai",
            DisplayName = "OpenAI",
            BaseUrl = "https://api.test.example.com/v1/",
            Protocol = EProviderProtocol.OpenAICompatible,
            HasModelDiscoveryApi = true
        });

        _settings = new AppSettings { ApiKey = "settings-key" };

        _handler = new CapturingHttpHandler();
        _httpClient = new HttpClient(_handler);

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _httpClientFactoryMock.Setup(f => f.CreateClient(HttpConstants.AiApiHttpClientName))
            .Returns(_httpClient);
    }

    [Fact]
    public void GetProvider_WithTransientKey_DoesNotMutateSettings()
    {
        // Arrange
        var factory = new AIProviderFactory(_settings, _httpClientFactoryMock.Object, _catalogMock.Object);

        // Act
        factory.GetProvider("openai", "transient-key");

        // Assert
        _settings.ApiKey.Should().Be("settings-key",
            "the two-arg overload must not mutate shared settings");
    }

    [Fact]
    public async Task GetProvider_WithTransientKey_SendsTransientKeyInAuthHeader()
    {
        // Arrange
        _handler.EnqueueResponse(ChatResponseJson);

        var factory = new AIProviderFactory(_settings, _httpClientFactoryMock.Object, _catalogMock.Object);
        var provider = factory.GetProvider("openai", "transient-key");

        // Act
        await provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "gpt-4",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hi" }]
        });

        // Assert
        _handler.LastRequest.Should().NotBeNull();
        var authHeader = _handler.LastRequest!.Headers.Authorization;
        authHeader.Should().NotBeNull();
        authHeader!.Parameter.Should().Be("transient-key",
            "the transient key should be used, not the settings key");
    }

    [Fact]
    public async Task GetProvider_WithSettingsKey_SendsSettingsKeyInAuthHeader()
    {
        // Arrange
        _handler.EnqueueResponse(ChatResponseJson);

        var factory = new AIProviderFactory(_settings, _httpClientFactoryMock.Object, _catalogMock.Object);
        var provider = factory.GetProvider("openai");

        // Act
        await provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "gpt-4",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hi" }]
        });

        // Assert
        _handler.LastRequest.Should().NotBeNull();
        var authHeader = _handler.LastRequest!.Headers.Authorization;
        authHeader.Should().NotBeNull();
        authHeader!.Parameter.Should().Be("settings-key");
    }

    [Fact]
    public void Factory_ImplementsITransientCredentialProviderFactory()
    {
        // Arrange & Act
        var factory = new AIProviderFactory(_settings, _httpClientFactoryMock.Object, _catalogMock.Object);

        // Assert
        factory.Should().BeAssignableTo<ITransientCredentialProviderFactory>();
        factory.Should().BeAssignableTo<IAIProviderFactory>();
    }

    [Fact]
    public void GetProvider_UnknownProvider_Throws()
    {
        // Arrange
        var factory = new AIProviderFactory(_settings, _httpClientFactoryMock.Object, _catalogMock.Object);

        // Act
        var act = () => factory.GetProvider("nonexistent-provider");

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*nonexistent-provider*");
    }

    [Fact]
    public async Task GetProvider_WithDecisionsEndpoint_BuildsDecisionProviderWithUnchangedChatEndpoint()
    {
        // Arrange — when a definition declares a decisions endpoint the factory builds the
        // decision-capable provider, but the chat surface must be unaffected: an endpoints
        // block (e.g. the migrated OpenRouter shape) must not change the resolved chat endpoint.
        _catalogMock.Setup(c => c.Get("openrouter")).Returns(new ProviderDefinition
        {
            Id = "openrouter",
            DisplayName = "OpenRouter",
            BaseUrl = "https://openrouter.test.example.com/api/v1/",
            Protocol = EProviderProtocol.OpenAICompatible,
            HasModelDiscoveryApi = true,
            Endpoints = new Dictionary<string, EndpointDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                ["decisions"] = new EndpointDefinition
                {
                    Path = "alpha/decisions",
                    BaseUrl = "https://openrouter.test.example.com/api/",
                    Protocol = EProviderProtocol.Decision
                }
            }
        });

        var factory = new AIProviderFactory(_settings, _httpClientFactoryMock.Object, _catalogMock.Object);
        var provider = factory.GetProvider("openrouter", "transient-key");

        // Act
        _handler.EnqueueResponse(ChatResponseJson);
        await provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "m1",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hi" }]
        });

        // Assert
        provider.Should().BeAssignableTo<AIProviderConnect.Providers.OpenAICompatibleDecisionProvider>();
        _handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://openrouter.test.example.com/api/v1/chat/completions");
    }

    private sealed class CapturingHttpHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public HttpRequestMessage? LastRequest { get; private set; }

        public void EnqueueResponse(string jsonBody)
        {
            _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonBody, System.Text.Encoding.UTF8, "application/json")
            });
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responses.Count > 0
                ? _responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
