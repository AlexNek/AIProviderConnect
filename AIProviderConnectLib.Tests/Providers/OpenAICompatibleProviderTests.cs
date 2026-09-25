using AIProviderConnect.Constants;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

public class OpenAICompatibleProviderTests
{
    [Theory]
    [InlineData("chat")]
    [InlineData("models")]
    [InlineData("stream")]
    public async Task Operations_UseBearerAuthAndDefaultHeaders(string operation)
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(
            operation == "stream" ? "data: [DONE]\n\n" : "{}");
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        // Act
        await InvokeAsync(provider, operation);

        // Assert
        handler.LastRequest!.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("fake-api-key");
        handler.LastRequest.Headers.GetValues("X-Test").Should().Equal("test-value");
        handler.LastRequest.RequestUri!.Host.Should().Be("test.example.com");
    }

    private static OpenAICompatibleProvider CreateProvider(HttpClient client) =>
        new(client, new HybridGatewayProviderOptions
        {
            BaseUrl = "https://test.example.com/v1",
            ApiKey = "fake-api-key",
            Enabled = true,
            DefaultHeaders = new Dictionary<string, string> { ["X-Test"] = "test-value" }
        }, new ProviderCatalog([
            new ProviderDefinition
            {
                Id = "test-provider", DisplayName = "Test provider",
                BaseUrl = "https://test.example.com/v1", Protocol = EProviderProtocol.HybridGateway
            }
        ]), "test-provider");

    private static async Task InvokeAsync(OpenAICompatibleProvider provider, string operation)
    {
        var request = new ChatCompletionRequest
        {
            Model = "test-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        };
        switch (operation)
        {
            case "chat":
                await provider.ChatAsync(request);
                break;
            case "models":
                await provider.GetModelsAsync();
                break;
            case "stream":
                await foreach (var _ in provider.StreamAsync(request)) { }
                break;
        }
    }
}
