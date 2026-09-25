using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;

using AiCleverness.Models;

using FluentAssertions;

using Moq;

using ScraperTool.Models;
using ScraperTool.Services.UrlResearch.Adapters;

using Xunit;

namespace ScraperTool.Tests;

public class AIProviderLlmClientTests
{
    [Fact]
    public async Task CompleteAsync_PropagatesReasoningContent_FromProviderResponse()
    {
        // Arrange
        var sut = CreateSut(new ChatCompletionResponse
        {
            Content = "answer",
            ReasoningContent = "step-by-step reasoning"
        });

        // Act
        var result = await sut.CompleteAsync(new[] { new LlmMessage("user", "question") });

        // Assert
        result.ReasoningContent.Should().Be("step-by-step reasoning");
        result.Content.Should().Be("answer");
    }

    [Fact]
    public async Task CompleteAsync_WithoutReasoningContent_LeavesReasoningNull()
    {
        // Arrange
        var sut = CreateSut(new ChatCompletionResponse { Content = "answer" });

        // Act
        var result = await sut.CompleteAsync(new[] { new LlmMessage("user", "question") });

        // Assert
        result.ReasoningContent.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_WithNullOptions_UsesPrimaryModelAndDefaultTemperature()
    {
        // Arrange
        ChatCompletionRequest? captured = null;
        var sut = CreateSut(
            new ChatCompletionResponse { Content = "answer" },
            onRequest: request => captured = request);

        // Act
        await sut.CompleteAsync(new[] { new LlmMessage("user", "question") });

        // Assert
        captured.Should().NotBeNull();
        captured!.Model.Should().Be("test-model");
        captured.Temperature.Should().Be(0.1f);
    }

    [Fact]
    public async Task CompleteAsync_WithOptionsModel_OverridesPrimaryModel()
    {
        // Arrange
        ChatCompletionRequest? captured = null;
        var sut = CreateSut(
            new ChatCompletionResponse { Content = "answer" },
            onRequest: request => captured = request);
        var options = new LlmCompletionOptions(Model: "override-model");

        // Act
        await sut.CompleteAsync(new[] { new LlmMessage("user", "question") }, options: options);

        // Assert
        captured.Should().NotBeNull();
        captured!.Model.Should().Be("override-model");
    }

    [Fact]
    public async Task CompleteAsync_WithOptionsTemperature_OverridesDefaultAndKeepsPrimaryModel()
    {
        // Arrange
        ChatCompletionRequest? captured = null;
        var sut = CreateSut(
            new ChatCompletionResponse { Content = "answer" },
            onRequest: request => captured = request);
        var options = new LlmCompletionOptions(Temperature: 0.5f);

        // Act
        await sut.CompleteAsync(new[] { new LlmMessage("user", "question") }, options: options);

        // Assert
        captured.Should().NotBeNull();
        captured!.Temperature.Should().Be(0.5f);
        captured.Model.Should().Be("test-model");
    }

    [Fact]
    public async Task CompleteAsync_WithNullTools_LeavesRequestToolsNull()
    {
        // Arrange
        ChatCompletionRequest? captured = null;
        var sut = CreateSut(
            new ChatCompletionResponse { Content = "answer" },
            onRequest: request => captured = request);

        // Act
        await sut.CompleteAsync(new[] { new LlmMessage("user", "question") });

        // Assert
        captured.Should().NotBeNull();
        captured!.Tools.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_WithTools_MapsEachToolDefinition()
    {
        // Arrange
        ChatCompletionRequest? captured = null;
        var sut = CreateSut(
            new ChatCompletionResponse { Content = "answer" },
            onRequest: request => captured = request);
        var tools = new[]
        {
            new AiCleverness.Models.ToolDefinition(
                "search_web", "Search the web", "{\"type\":\"object\"}"),
            new AiCleverness.Models.ToolDefinition("no_schema", "No schema", null)
        };

        // Act
        await sut.CompleteAsync(new[] { new LlmMessage("user", "question") }, tools);

        // Assert
        captured.Should().NotBeNull();
        captured!.Tools.Should().NotBeNull();
        captured.Tools!.Should().HaveCount(2);
        captured.Tools[0].Name.Should().Be("search_web");
        captured.Tools[0].Description.Should().Be("Search the web");
        captured.Tools[0].Parameters.GetProperty("type").GetString().Should().Be("object");
        captured.Tools[1].Name.Should().Be("no_schema");
        captured.Tools[1].Parameters.GetRawText().Should().Be("{}");
    }

    [Fact]
    public async Task CompleteAsync_MapsMessageRoles()
    {
        // Arrange
        ChatCompletionRequest? captured = null;
        var sut = CreateSut(
            new ChatCompletionResponse { Content = "answer" },
            onRequest: request => captured = request);
        var messages = new[]
        {
            new LlmMessage("system", "s"),
            new LlmMessage("user", "u"),
            new LlmMessage("assistant", "a"),
            new LlmMessage("tool", "t"),
            new LlmMessage("unknown", "w")
        };

        // Act
        await sut.CompleteAsync(messages);

        // Assert
        captured.Should().NotBeNull();
        captured!.Messages.Should().HaveCount(5);
        captured.Messages[0].Role.Should().Be(EChatRole.System);
        captured.Messages[0].Content.Should().Be("s");
        captured.Messages[1].Role.Should().Be(EChatRole.User);
        captured.Messages[2].Role.Should().Be(EChatRole.Assistant);
        captured.Messages[3].Role.Should().Be(EChatRole.Tool);
        captured.Messages[4].Role.Should().Be(EChatRole.User);
    }

    [Fact]
    public async Task CompleteAsync_MapsMessageToolCallsAndToolCallId()
    {
        // Arrange
        ChatCompletionRequest? captured = null;
        var sut = CreateSut(
            new ChatCompletionResponse { Content = "answer" },
            onRequest: request => captured = request);
        var assistant = new LlmMessage("assistant", null)
        {
            ToolCalls = new[] { new LlmToolCall("call-1", "search_web", "{\"q\":\"x\"}") }
        };
        var toolResult = new LlmMessage("tool", "result") { ToolCallId = "call-1" };

        // Act
        await sut.CompleteAsync(new[] { assistant, toolResult });

        // Assert
        captured.Should().NotBeNull();
        var call = captured!.Messages[0].ToolCalls!.Should().ContainSingle().Subject;
        call.Id.Should().Be("call-1");
        call.Name.Should().Be("search_web");
        call.Arguments.Should().Be("{\"q\":\"x\"}");
        captured.Messages[1].ToolCallId.Should().Be("call-1");
        captured.Messages[1].ToolCalls.Should().BeNull();
    }

    [Fact]
    public async Task CompleteAsync_NullProviderResponse_ReturnsNoResponseFallback()
    {
        // Arrange
        var sut = CreateSut(null);

        // Act
        var result = await sut.CompleteAsync(new[] { new LlmMessage("user", "question") });

        // Assert
        result.Content.Should().BeNull();
        result.ToolCalls.Should().BeNull();
        result.FinishReason.Should().Be("no_response");
    }

    [Fact]
    public async Task CompleteAsync_PropagatesTokenUsage()
    {
        // Arrange
        var sut = CreateSut(new ChatCompletionResponse
        {
            Content = "answer",
            Usage = new UsageInfo { PromptTokens = 11, CompletionTokens = 7 }
        });

        // Act
        var result = await sut.CompleteAsync(new[] { new LlmMessage("user", "question") });

        // Assert
        result.Usage.Should().NotBeNull();
        result.Usage!.PromptTokens.Should().Be(11);
        result.Usage.CompletionTokens.Should().Be(7);
    }

    [Fact]
    public async Task CompleteAsync_PropagatesToolCalls()
    {
        // Arrange
        var sut = CreateSut(new ChatCompletionResponse
        {
            Content = string.Empty,
            ToolCalls = new List<ToolCall>
            {
                new() { Id = "call-9", Name = "fetch_url", Arguments = "{}" }
            }
        });

        // Act
        var result = await sut.CompleteAsync(new[] { new LlmMessage("user", "question") });

        // Assert
        var call = result.ToolCalls!.Should().ContainSingle().Subject;
        call.Id.Should().Be("call-9");
        call.Name.Should().Be("fetch_url");
        call.Arguments.Should().Be("{}");
    }

    [Fact]
    public async Task StreamAsync_ProviderNotStreaming_ThrowsNotSupportedException()
    {
        // Arrange
        var sut = CreateSut(new ChatCompletionResponse { Content = "answer" });

        // Act
        Func<Task> act = async () =>
        {
            await foreach (var _ in sut.StreamAsync(new[] { new LlmMessage("user", "question") }))
            {
            }
        };

        // Assert
        await act.Should()
            .ThrowAsync<NotSupportedException>()
            .WithMessage("*test-provider*");
    }

    private static AIProviderLlmClient CreateSut(
        ChatCompletionResponse? response,
        AppSettings? settings = null,
        Action<ChatCompletionRequest>? onRequest = null)
    {
        var mockProvider = new Mock<IAIProvider>();
        mockProvider
            .Setup(p => p.ChatAsync(It.IsAny<ChatCompletionRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChatCompletionRequest, CancellationToken>((request, _) => onRequest?.Invoke(request))
            .ReturnsAsync(response!);

        var mockFactory = new Mock<IAIProviderFactory>();
        mockFactory
            .Setup(f => f.GetProvider(It.IsAny<string>()))
            .Returns(mockProvider.Object);

        settings ??= new AppSettings
        {
            ApiKey = "fake-api-key",
            SelectedProviderId = "test-provider",
            PrimaryModel = "test-model"
        };

        return new AIProviderLlmClient(mockFactory.Object, settings);
    }
}
