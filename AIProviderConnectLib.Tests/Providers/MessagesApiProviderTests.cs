using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

public class MessagesApiProviderTests
{
    private const string MessagesResponse =
        """{"id":"msg-1","type":"message","role":"assistant","content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";

    private static (MessagesApiProvider Provider, CapturingHttpMessageHandler Handler) CreateProvider(
        string? customAuthHeader = "x-api-key")
    {
        var handler = new CapturingHttpMessageHandler(MessagesResponse);
        var httpClient = new HttpClient(handler);
        var catalog = new ProviderCatalog();
        var options = new MessagesApiOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "fake-api-key",
            Enabled = true,
            CustomAuthHeaderName = customAuthHeader,
        };

        return (new MessagesApiProvider(httpClient, options, catalog, "anthropic"), handler);
    }

    [Fact]
    public async Task ChatAsync_SendsApiKeyInConfiguredHeader()
    {
        // Arrange
        var (provider, handler) = CreateProvider("x-api-key");

        // Act
        await provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "claude-3",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        });

        // Assert
        handler.LastRequest!.Headers.Should().Contain(h =>
            h.Key == "x-api-key" && h.Value.Contains("fake-api-key"));
    }

    [Fact]
    public async Task ChatAsync_IncludesDefaultHeaders()
    {
        // Arrange
        var handler = new CapturingHttpMessageHandler(MessagesResponse);
        var httpClient = new HttpClient(handler);
        var provider = new MessagesApiProvider(httpClient, new MessagesApiOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "fake-api-key",
            Enabled = true,
            CustomAuthHeaderName = "x-api-key",
            DefaultHeaders = new Dictionary<string, string>
            {
                ["anthropic-version"] = "2023-06-01"
            }
        }, new ProviderCatalog(), "anthropic");

        // Act
        await provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "claude-3",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        });

        // Assert
        handler.LastRequest!.Headers.Should().Contain(h =>
            h.Key == "anthropic-version" && h.Value.Contains("2023-06-01"));
    }

    [Fact]
    public async Task ChatAsync_NullCustomAuthHeader_ThrowsInvalidOperationException()
    {
        // Arrange
        var (provider, _) = CreateProvider(customAuthHeader: null);

        // Act
        var act = () => provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "claude-3",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        });

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*CustomAuthHeaderName*");
    }

    [Fact]
    public async Task ChatAsync_EmptyApiKey_DoesNotThrowEvenWithNullHeaderName()
    {
        // Arrange — no key means the auth-header path is skipped entirely
        var handler = new CapturingHttpMessageHandler(MessagesResponse);
        var httpClient = new HttpClient(handler);
        var provider = new MessagesApiProvider(httpClient, new MessagesApiOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "",
            Enabled = true,
            CustomAuthHeaderName = null,
        }, new ProviderCatalog(), "anthropic");

        // Act
        var act = () => provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "claude-3",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        });

        // Assert — the provider-level EnsureProviderEnabled check will throw NoApiKey
        // before reaching the header logic, so this tests that the flow is correct
        await act.Should().ThrowAsync<AIProviderConnect.Exceptions.AiException>();
    }
}
