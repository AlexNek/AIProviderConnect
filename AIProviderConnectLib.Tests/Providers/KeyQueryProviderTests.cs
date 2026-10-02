using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

public class KeyQueryProviderTests
{
    private const string GeminiResponse =
        """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""";

    private static (KeyQueryProvider Provider, CapturingHttpMessageHandler Handler) CreateProvider()
    {
        var handler = new CapturingHttpMessageHandler(GeminiResponse);
        var httpClient = new HttpClient(handler);
        var catalog = new ProviderCatalog();
        var options = new KeyQueryOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "fake-api-key",
            Enabled = true,
            CustomAuthHeaderName = "x-goog-api-key"
        };

        return (new KeyQueryProvider(httpClient, options, catalog, "gemini"), handler);
    }

    [Fact]
    public async Task ChatAsync_NonAbsoluteBaseUrl_ThrowsInvalidRequest()
    {
        // Arrange — a scheme-less base URL is rejected with a structured AiException rather than
        // leaking a raw UriFormatException from request construction
        var handler = new CapturingHttpMessageHandler(GeminiResponse);
        var provider = new KeyQueryProvider(new HttpClient(handler), new KeyQueryOptions
        {
            BaseUrl = "relative.example.com/v1/",
            ApiKey = "fake-api-key",
            Enabled = true,
            CustomAuthHeaderName = "x-goog-api-key"
        }, new ProviderCatalog(), "gemini");

        // Act
        Func<Task> act = () => provider.ChatAsync(new ChatCompletionRequest
        {
            Model = "test-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        });

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.InvalidRequest);
        ex.Message.Should().Contain("is not a valid absolute URL");
    }

    [Theory]
    [InlineData("https://test.example.com/v1", "chat", "models/test-model:generateContent")]
    [InlineData("https://test.example.com/v1/", "chat", "models/test-model:generateContent")]
    [InlineData("https://test.example.com/v1", "models", "models")]
    [InlineData("https://test.example.com/v1/", "models", "models")]
    [InlineData("https://test.example.com/v1", "stream", "models/test-model:streamGenerateContent?alt=sse")]
    [InlineData("https://test.example.com/v1/", "stream", "models/test-model:streamGenerateContent?alt=sse")]
    public async Task Operations_NormalizeBaseUrlAndSendKeyOnlyInHeader(
        string baseUrl, string operation, string endpoint)
    {
        // Arrange
        var response = operation == "stream" ? "data: [DONE]\n\n" : GeminiResponse;
        using var handler = new CapturingHttpMessageHandler(response);
        using var client = new HttpClient(handler);
        var provider = new KeyQueryProvider(client, new KeyQueryOptions
        {
            BaseUrl = baseUrl, ApiKey = "fake-api-key", Enabled = true,
            CustomAuthHeaderName = "x-goog-api-key",
            DefaultHeaders = new Dictionary<string, string> { ["X-Test"] = "test-value" }
        }, new ProviderCatalog(), "gemini");

        // Act
        await InvokeAsync(provider, operation);

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri.Should().Be("https://test.example.com/v1/" + endpoint);
        handler.LastRequest.RequestUri.AbsoluteUri.Should().NotContain("fake-api-key");
        handler.LastRequest.Headers.GetValues("x-goog-api-key").Should().Equal("fake-api-key");
        handler.LastRequest.Headers.GetValues("X-Test").Should().Equal("test-value");
        handler.LastRequest.Headers.Authorization.Should().BeNull();
    }

    [Theory]
    [InlineData("chat", "custom/path/{model}:doStuff", "custom/path/test-model:doStuff")]
    [InlineData("models", "custom/models", "custom/models")]
    [InlineData("stream", "custom/path/{model}:stream", "custom/path/test-model:stream?alt=sse")]
    public async Task Operations_UseCustomEndpointPatternsFromOptions(
        string operation, string chatOrStreamPattern, string expectedEndpoint)
    {
        // Arrange
        var response = operation == "stream" ? "data: [DONE]\n\n" : GeminiResponse;
        using var handler = new CapturingHttpMessageHandler(response);
        using var client = new HttpClient(handler);
        var options = new KeyQueryOptions
        {
            BaseUrl = "https://test.example.com/v1/",
            ApiKey = "fake-api-key", Enabled = true, CustomAuthHeaderName = "x-goog-api-key",
            ChatEndpoint = operation == "stream"
                ? EndpointDefaults.KeyQuery.GenerateContent
                : chatOrStreamPattern,
            StreamEndpoint = operation == "stream"
                ? chatOrStreamPattern
                : EndpointDefaults.KeyQuery.StreamGenerateContent,
            ModelsEndpoint = operation == "models" ? chatOrStreamPattern : EndpointDefaults.Models
        };
        var provider = new KeyQueryProvider(client, options, new ProviderCatalog(), "gemini");

        // Act
        await InvokeAsync(provider, operation);

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri.Should()
            .Be("https://test.example.com/v1/" + expectedEndpoint);
    }

    [Theory]
    [InlineData("chat")]
    [InlineData("models")]
    [InlineData("stream")]
    public async Task Operations_ReportConfigurationErrorsBeforeSending(string operation)
    {
        foreach (var (enabled, baseUrl, apiKey, expectedCode) in new[]
        {
            (false, "", "", AiErrorCodes.ProviderDisabled),
            (true, "", "", AiErrorCodes.NoBaseUrl),
            (true, "https://test.example.com/v1", " ", AiErrorCodes.NoApiKey)
        })
        {
            // Arrange
            using var handler = new CapturingHttpMessageHandler(GeminiResponse);
            using var client = new HttpClient(handler);
            var provider = new KeyQueryProvider(client, new KeyQueryOptions
            {
                Enabled = enabled, BaseUrl = baseUrl, ApiKey = apiKey
            }, new ProviderCatalog(), "gemini");

            // Act
            Func<Task> act = () => InvokeAsync(provider, operation);

            // Assert
            (await act.Should().ThrowAsync<AiException>()).Which.Code.Should().Be(expectedCode);
            handler.LastRequest.Should().BeNull();
        }
    }

    private static async Task InvokeAsync(KeyQueryProvider provider, string operation)
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

    private static JsonElement FirstContentParts(string body)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("contents")[0].GetProperty("parts").Clone();
    }

    [Fact]
    public async Task ChatAsync_TextOnly_SerializesSingleTextPart()
    {
        // Arrange
        var (provider, handler) = CreateProvider();
        var request = new ChatCompletionRequest
        {
            Model = "gemini-model",
            Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
        };

        // Act
        await provider.ChatAsync(request);

        // Assert
        var parts = FirstContentParts(handler.CapturedBody!);
        parts.GetArrayLength().Should().Be(1);
        parts[0].GetProperty("text").GetString().Should().Be("Hello");
    }

    [Fact]
    public async Task ChatAsync_WithImageBytes_SerializesInlineDataPart()
    {
        // Arrange
        var (provider, handler) = CreateProvider();
        var bytes = new byte[] { 1, 2, 3 };
        var request = new ChatCompletionRequest
        {
            Model = "gemini-model",
            Messages =
            [
                new ChatMessage
                {
                    Role = EChatRole.User,
                    ContentParts =
                    [
                        new ContentPart { Type = "text", Text = "Describe this image." },
                        new ContentPart
                        {
                            Type = "image_url",
                            Image = ImageContent.FromBytes(bytes, "image/png")
                        }
                    ]
                }
            ]
        };

        // Act
        await provider.ChatAsync(request);

        // Assert
        var parts = FirstContentParts(handler.CapturedBody!);
        parts.GetArrayLength().Should().Be(2);
        parts[0].GetProperty("text").GetString().Should().Be("Describe this image.");
        var inline = parts[1].GetProperty("inlineData");
        inline.GetProperty("mimeType").GetString().Should().Be("image/png");
        inline.GetProperty("data").GetString().Should().Be(Convert.ToBase64String(bytes));
    }

    [Fact]
    public async Task ChatAsync_WithImageUrl_SerializesFileDataPart()
    {
        // Arrange
        var (provider, handler) = CreateProvider();
        var request = new ChatCompletionRequest
        {
            Model = "gemini-model",
            Messages =
            [
                new ChatMessage
                {
                    Role = EChatRole.User,
                    ContentParts =
                    [
                        new ContentPart
                        {
                            Type = "image_url",
                            Image = ImageContent.FromUrl("https://test.example.com/img.png")
                        }
                    ]
                }
            ]
        };

        // Act
        await provider.ChatAsync(request);

        // Assert
        var parts = FirstContentParts(handler.CapturedBody!);
        var file = parts[0].GetProperty("fileData");
        file.GetProperty("fileUri").GetString().Should().Be("https://test.example.com/img.png");
        file.TryGetProperty("mimeType", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ChatAsync_SystemMessageWithContentParts_SerializesSystemInstructionText()
    {
        // Arrange — a system message carrying only ContentParts must not produce an empty
        // systemInstruction; its text parts are concatenated and image parts are skipped.
        var (provider, handler) = CreateProvider();
        var request = new ChatCompletionRequest
        {
            Model = "gemini-model",
            Messages =
            [
                new ChatMessage
                {
                    Role = EChatRole.System,
                    ContentParts =
                    [
                        new ContentPart { Type = "text", Text = "You are helpful." },
                        new ContentPart
                        {
                            Type = "image_url",
                            Image = ImageContent.FromUrl("https://test.example.com/img.png")
                        }
                    ]
                },
                new ChatMessage { Role = EChatRole.User, Content = "Hi" }
            ]
        };

        // Act
        await provider.ChatAsync(request);

        // Assert
        using var doc = JsonDocument.Parse(handler.CapturedBody!);
        var systemParts = doc.RootElement.GetProperty("systemInstruction").GetProperty("parts");
        systemParts[0].GetProperty("text").GetString().Should().Be("You are helpful.");
    }
}
