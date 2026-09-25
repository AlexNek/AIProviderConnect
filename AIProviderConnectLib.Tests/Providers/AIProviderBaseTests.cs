using System.Net;
using System.Reflection;

using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIProviderConnectLib.Tests.Providers;

public class AIProviderBaseTests
{
    private static OpenAICompatibleProvider CreateProvider(
        HttpMessageHandler handler,
        int maxRetryCount = 0,
        ILogger? logger = null) =>
        new(
            new HttpClient(handler),
            new OpenAICompatibleProviderOptions
            {
                BaseUrl = "https://test.example.com/v1",
                ApiKey = "fake-api-key",
                Enabled = true,
                MaxRetryCount = maxRetryCount,
                RetryDelay = TimeSpan.FromMilliseconds(1)
            },
            new ProviderCatalog([
                new ProviderDefinition
                {
                    Id = "test-provider", DisplayName = "Test provider",
                    BaseUrl = "https://test.example.com/v1", Protocol = EProviderProtocol.OpenAICompatible
                }
            ]),
            "test-provider",
            logger);

    private static ChatCompletionRequest SampleRequest() => new()
    {
        Model = "test-model",
        Messages = [new ChatMessage { Role = EChatRole.User, Content = "Hello" }]
    };

    [Theory]
    [InlineData(400, AiErrorCodes.InvalidRequest, "The request was rejected — check that the configured model name and base URL are correct.")]
    [InlineData(401, AiErrorCodes.Unauthorized, "Authentication failed. Check that your API key is correct.")]
    [InlineData(402, AiErrorCodes.ProviderCallFailed, "Payment required. The provider requires billing to be set up before use.")]
    [InlineData(403, AiErrorCodes.Forbidden, "Access denied. Your API key may lack permissions for the requested model.")]
    [InlineData(404, AiErrorCodes.EndpointNotFound, "Endpoint not found. Check that the configured base URL and model name are correct.")]
    [InlineData(405, AiErrorCodes.ProviderCallFailed, "The provider rejected the request method. Try a different provider or model.")]
    [InlineData(408, AiErrorCodes.ProviderCallFailed, "Request timed out. Check your network connection to the provider.")]
    [InlineData(409, AiErrorCodes.ProviderCallFailed, "Conflict — the request conflicts with the provider's current state.")]
    [InlineData(415, AiErrorCodes.ProviderCallFailed, "Unsupported request format. The selected AI model may not be compatible.")]
    [InlineData(422, AiErrorCodes.ProviderCallFailed, "Invalid request data. Check the configured request values.")]
    [InlineData(429, AiErrorCodes.RateLimited, "Too many requests — the model is rate-limited. Wait a moment and try again, or switch to another model.")]
    [InlineData(500, AiErrorCodes.NoServer, "The provider's server encountered an error. Try again, or switch to another model.")]
    [InlineData(502, AiErrorCodes.NoServer, "The model is temporarily unavailable (high demand or server error). Wait a moment and try again, or switch to another model.")]
    [InlineData(503, AiErrorCodes.NoServer, "The model is temporarily unavailable (high demand or server error). Wait a moment and try again, or switch to another model.")]
    [InlineData(504, AiErrorCodes.NoServer, "The model did not respond in time (gateway timeout). Wait a moment and try again, or switch to another model.")]
    [InlineData(418, AiErrorCodes.ProviderCallFailed, "Unexpected error (code 418). Check the provider configuration.")]
    public async Task HttpErrors_KeepCodeMappingAndUseConsumerNeutralMessages(
        int statusCode, string expectedCode, string expectedMessage)
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler("{}") { StatusCode = (HttpStatusCode)statusCode };
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = () => provider.GetModelsAsync();

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(expectedCode);
        exception.Message.Should().Be($"HTTP {statusCode}: {expectedMessage}");
        exception.Message.Should().NotContain("AI Setup");
    }

    [Fact]
    public async Task GetModelsAsync_MaxRetryCountIsZero_DoesNotRetry()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler("{}", HttpStatusCode.InternalServerError);
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = () => provider.GetModelsAsync();

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.NoServer);
        handler.CallCount.Should().Be(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetModelsAsync_TransientError_RetriesUntilItSucceeds(HttpStatusCode firstStatus)
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler(
            """{"data":[{"id":"test-model"}]}""", firstStatus, HttpStatusCode.OK);
        var provider = CreateProvider(handler, maxRetryCount: 2);

        // Act
        var models = await provider.GetModelsAsync();

        // Assert
        handler.CallCount.Should().Be(2);
        models.Single().Id.Should().Be("test-model");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AiErrorCodes.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, AiErrorCodes.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, AiErrorCodes.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, AiErrorCodes.EndpointNotFound)]
    public async Task GetModelsAsync_NonRetryableError_DoesNotRetry(
        HttpStatusCode status, string expectedCode)
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler("{}", status, HttpStatusCode.OK);
        var provider = CreateProvider(handler, maxRetryCount: 2);

        // Act
        Func<Task> act = () => provider.GetModelsAsync();

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(expectedCode);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetModelsAsync_RetriesExhausted_StopsAfterMaxRetryCountExtraAttempts()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler("{}", HttpStatusCode.TooManyRequests);
        var provider = CreateProvider(handler, maxRetryCount: 2);

        // Act
        Func<Task> act = () => provider.GetModelsAsync();

        // Assert
        (await act.Should().ThrowAsync<AiException>()).Which.Code.Should().Be(AiErrorCodes.RateLimited);
        handler.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task ChatAsync_TransientError_RetriesUntilItSucceeds()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler(
            """{"choices":[{"message":{"content":"hi"},"finish_reason":"stop"}]}""",
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var provider = CreateProvider(handler, maxRetryCount: 1);

        // Act
        var response = await provider.ChatAsync(SampleRequest());

        // Assert
        handler.CallCount.Should().Be(2);
        response.Content.Should().Be("hi");
    }

    [Fact]
    public async Task StreamAsync_RateLimited_IsNotRetried()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler("{}", HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var provider = CreateProvider(handler, maxRetryCount: 2);

        // Act
        Func<Task> act = async () =>
        {
            await foreach (var _ in provider.StreamAsync(SampleRequest()))
            {
            }
        };

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.RateLimited);
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RetryLogsWarning_RenderDelayCodeAndMessageInOrder()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler("{}", HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var logger = new RecordingLogger();
        var provider = CreateProvider(handler, maxRetryCount: 2, logger);

        // Act
        await provider.GetModelsAsync();

        // Assert — {Delay} carries milliseconds, {Code} the error code, {Message} the exception text
        var retries = logger.MessagesAt(LogLevel.Warning).ToList();
        retries.Should().HaveCount(1);
        retries[0].Should().MatchRegex(
            @"^Provider 'test-provider': retry 1/2 after \d+(\.\d+)?ms — ai/rate-limited: HTTP 429: ");
    }

    [Fact]
    public async Task RequestFailure_RateLimited_LogsWarningNotError()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler("{}", HttpStatusCode.TooManyRequests);
        var logger = new RecordingLogger();
        var provider = CreateProvider(handler, logger: logger);

        // Act
        Func<Task> act = () => provider.GetModelsAsync();
        await act.Should().ThrowAsync<AiException>();

        // Assert
        logger.MessagesAt(LogLevel.Warning).Should().ContainSingle()
            .Which.Should().Be("Provider 'test-provider': request to models was rate-limited");
        logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task RequestFailure_NonRetryable_LogsErrorWithoutClaimingRetries()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler("{}", HttpStatusCode.Unauthorized);
        var logger = new RecordingLogger();
        var provider = CreateProvider(handler, logger: logger);

        // Act
        Func<Task> act = () => provider.GetModelsAsync();
        await act.Should().ThrowAsync<AiException>();

        // Assert
        var errors = logger.MessagesAt(LogLevel.Error).ToList();
        errors.Should().ContainSingle();
        errors[0].Should().Be("Provider 'test-provider': request to models failed");
        logger.Entries.Should().NotContain(entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task StreamAsync_MalformedSseData_SkipsChunkAndContinues()
    {
        // Arrange — valid chunk, then a non-JSON data line, then another valid chunk
        var sseBody =
            """
            data: {"choices":[{"delta":{"content":"Hello"}}]}

            data: this-is-not-json

            data: {"choices":[{"delta":{"content":"World"}}]}

            data: [DONE]
            """;
        using var handler = new CapturingHttpMessageHandler(sseBody);
        var logger = new RecordingLogger();
        var provider = CreateProvider(handler, logger: logger);

        // Act
        var chunks = new List<StreamingChatChunk>();
        await foreach (var chunk in provider.StreamAsync(SampleRequest()))
        {
            chunks.Add(chunk);
        }

        // Assert — two valid chunks yielded, malformed line skipped without throwing
        chunks.Should().HaveCount(2);
        chunks[0].Content.Should().Be("Hello");
        chunks[1].Content.Should().Be("World");
        logger.MessagesAt(LogLevel.Warning).Should().ContainSingle()
            .Which.Should().Contain("skipping malformed SSE data");
    }

    [Fact]
    public void Constructor_WithoutLogger_UsesNullLoggerInstance()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler("{}", HttpStatusCode.OK);
        var provider = CreateProvider(handler);
        var loggerProperty = typeof(AIProviderBase)
            .GetProperty("Logger", BindingFlags.NonPublic | BindingFlags.Instance);

        // Act
        var logger = loggerProperty!.GetValue(provider);

        // Assert
        logger.Should().BeSameAs(NullLogger.Instance);
    }

    [Fact]
    public async Task ChatAsync_HttpRequestException_TranslatedToNoConnection()
    {
        // Arrange
        using var handler = new ThrowingHttpMessageHandler(new HttpRequestException("DNS failure"));
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = () => provider.ChatAsync(SampleRequest());

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.NoConnection);
    }

    [Fact]
    public async Task GetModelsAsync_TimeoutTaskCanceledException_TranslatedToTimeout()
    {
        // Arrange — TaskCanceledException without the caller's token cancelled simulates HttpClient timeout
        using var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("HttpClient timeout"));
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = () => provider.GetModelsAsync();

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.Timeout);
    }

    [Fact]
    public async Task ChatAsync_UserCancellation_PropagatesAsOperationCanceledException()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        using var handler = new ThrowingHttpMessageHandler(new TaskCanceledException("cancelled", new OperationCanceledException(cts.Token)));
        var provider = CreateProvider(handler);
        cts.Cancel();

        // Act
        Func<Task> act = () => provider.ChatAsync(SampleRequest(), cts.Token);

        // Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetModelsAsync_NoConnection_IsRetriedByPollyPipeline()
    {
        // Arrange — first call throws, second call succeeds
        using var handler = new ScriptedThrowHttpMessageHandler(
            new HttpRequestException("connection refused"),
            successResponseJson: """{"data":[{"id":"test-model"}]}""");
        var provider = CreateProvider(handler, maxRetryCount: 2);

        // Act
        var models = await provider.GetModelsAsync();

        // Assert
        handler.CallCount.Should().Be(2);
        models.Single().Id.Should().Be("test-model");
    }

    [Fact]
    public async Task StreamAsync_HttpRequestException_TranslatedToNoConnection()
    {
        // Arrange
        using var handler = new ThrowingHttpMessageHandler(new HttpRequestException("DNS failure"));
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = async () =>
        {
            await foreach (var _ in provider.StreamAsync(SampleRequest()))
            {
            }
        };

        // Assert
        var exception = (await act.Should().ThrowAsync<AiException>()).Which;
        exception.Code.Should().Be(AiErrorCodes.NoConnection);
    }
}
