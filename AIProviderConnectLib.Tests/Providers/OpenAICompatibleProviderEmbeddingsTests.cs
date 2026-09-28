using System.Net;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Constants;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Providers;
using AIProviderConnect.Services;
using AIProviderConnect.Tests.TestDoubles;

using FluentAssertions;

namespace AIProviderConnectLib.Tests.Providers;

/// <summary>
/// Hermetic tests for the embeddings capability on <see cref="OpenAICompatibleProvider"/>:
/// endpoint URL, authentication, model fallback, HTTP error classification, retry, and
/// capability discovery via <c>is IEmbeddingProvider</c>.
/// </summary>
public class OpenAICompatibleProviderEmbeddingsTests
{
    private static readonly string ValidEmbeddingResponse = """
        {
          "model": "text-embedding-3-small",
          "data": [{ "index": 0, "embedding": [0.1, 0.2] }],
          "usage": { "prompt_tokens": 3 }
        }
        """;

    private static OpenAICompatibleProvider CreateProvider(
        HttpMessageHandler handler,
        string? defaultEmbeddingModel = null,
        int maxRetryCount = 0) =>
        new(
            new HttpClient(handler),
            new OpenAICompatibleProviderOptions
            {
                BaseUrl = "https://test.example.com/v1",
                ApiKey = "fake-api-key",
                Enabled = true,
                DefaultEmbeddingModel = defaultEmbeddingModel ?? string.Empty,
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
            "test-provider");

    private static EmbeddingRequest SampleRequest(string model = "embed-model") =>
        new() { Model = model, Input = ["hello"] };

    // --- Endpoint and authentication ---

    [Fact]
    public async Task EmbedAsync_UsesEmbeddingsEndpointWithBearerAuth()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ValidEmbeddingResponse);
        var provider = CreateProvider(handler);

        // Act
        await provider.EmbedAsync(SampleRequest());

        // Assert
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/embeddings");
        handler.LastRequest.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be("fake-api-key");
    }

    // --- Model fallback ---

    [Fact]
    public async Task EmbedAsync_EmptyModel_FallsBackToDefaultEmbeddingModel()
    {
        // Arrange — request has empty Model, provider has DefaultEmbeddingModel set
        using var handler = new CapturingHttpMessageHandler(ValidEmbeddingResponse);
        var provider = CreateProvider(handler, defaultEmbeddingModel: "fallback-embed-model");
        var request = new EmbeddingRequest { Model = string.Empty, Input = ["hello"] };

        // Act
        await provider.EmbedAsync(request);

        // Assert — the wire body should contain the fallback model
        var body = JsonDocument.Parse(handler.CapturedBody!);
        body.RootElement.GetProperty("model").GetString().Should().Be("fallback-embed-model");
    }

    [Fact]
    public async Task EmbedAsync_BothModelsEmpty_ThrowsEmbeddingModelNotConfigured()
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler(ValidEmbeddingResponse);
        var provider = CreateProvider(handler); // no DefaultEmbeddingModel
        var request = new EmbeddingRequest { Model = string.Empty, Input = ["hello"] };

        // Act
        Func<Task> act = () => provider.EmbedAsync(request);

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingModelNotConfigured);
    }

    // --- Response alignment against the request input ---

    [Fact]
    public async Task EmbedAsync_VectorCountDoesNotMatchInput_ThrowsEmbeddingFailed()
    {
        // Arrange — two inputs, but the provider returns a single vector
        using var handler = new CapturingHttpMessageHandler("""
            { "data": [{ "index": 0, "embedding": [0.1, 0.2] }] }
            """);
        var provider = CreateProvider(handler);
        var request = new EmbeddingRequest { Model = "embed-model", Input = ["hello", "world"] };

        // Act
        Func<Task> act = () => provider.EmbedAsync(request);

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
        ex.Message.Should().Contain("1 vectors for 2 input strings");
    }

    [Fact]
    public async Task EmbedAsync_IndicesNotZeroBased_ThrowsEmbeddingFailed()
    {
        // Arrange — two inputs and two vectors, but the provider numbers them 1 and 2
        using var handler = new CapturingHttpMessageHandler("""
            {
              "data": [
                { "index": 1, "embedding": [0.1, 0.2] },
                { "index": 2, "embedding": [0.3, 0.4] }
              ]
            }
            """);
        var provider = CreateProvider(handler);
        var request = new EmbeddingRequest { Model = "embed-model", Input = ["hello", "world"] };

        // Act
        Func<Task> act = () => provider.EmbedAsync(request);

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
        ex.Message.Should().Contain("the entry at position 0 has index 1");
    }

    [Fact]
    public async Task EmbedAsync_InputAndResponseAligned_ReturnsVectorsInInputOrder()
    {
        // Arrange — indices cover 0..n-1 but arrive swapped in the payload
        using var handler = new CapturingHttpMessageHandler("""
            {
              "data": [
                { "index": 1, "embedding": [0.3, 0.4] },
                { "index": 0, "embedding": [0.1, 0.2] }
              ]
            }
            """);
        var provider = CreateProvider(handler);
        var request = new EmbeddingRequest { Model = "embed-model", Input = ["hello", "world"] };

        // Act
        var response = await provider.EmbedAsync(request);

        // Assert — alignment accepted, re-sorted back to input order
        response.Data.Should().HaveCount(2);
        response.Data[0].Index.Should().Be(0);
        response.Data[0].Embedding.Should().Equal(0.1f, 0.2f);
        response.Data[1].Index.Should().Be(1);
        response.Data[1].Embedding.Should().Equal(0.3f, 0.4f);
    }

    // --- HTTP error classification (X1-IMP-002) ---

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(422)]
    public async Task EmbedAsync_ClientErrorClassifiedAsEmbeddingFailed(int statusCode)
    {
        // Arrange
        using var handler = new CapturingHttpMessageHandler("{}")
        { StatusCode = (HttpStatusCode)statusCode };
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = () => provider.EmbedAsync(SampleRequest());

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.EmbeddingFailed);
    }

    [Fact]
    public async Task EmbedAsync_RateLimited_ClassifiedAsRateLimitedNotEmbeddingFailed()
    {
        // Arrange — 429 must remain retry-triggering, not be remapped to EmbeddingFailed
        using var handler = new CapturingHttpMessageHandler("{}")
        { StatusCode = HttpStatusCode.TooManyRequests };
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = () => provider.EmbedAsync(SampleRequest());

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.RateLimited);
    }

    [Fact]
    public async Task EmbedAsync_ServerError_ClassifiedAsNoServerNotEmbeddingFailed()
    {
        // Arrange — 500 must remain retry-triggering
        using var handler = new CapturingHttpMessageHandler("{}")
        { StatusCode = HttpStatusCode.InternalServerError };
        var provider = CreateProvider(handler);

        // Act
        Func<Task> act = () => provider.EmbedAsync(SampleRequest());

        // Assert
        var ex = (await act.Should().ThrowAsync<AiException>()).Which;
        ex.Code.Should().Be(AiErrorCodes.NoServer);
    }

    // --- Retry ---

    [Fact]
    public async Task EmbedAsync_RateLimitedThenSuccess_RetriesAndSucceeds()
    {
        // Arrange
        using var handler = new ScriptedStatusHttpMessageHandler(
            ValidEmbeddingResponse, HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        var provider = CreateProvider(handler, maxRetryCount: 1);

        // Act
        var response = await provider.EmbedAsync(SampleRequest());

        // Assert
        handler.CallCount.Should().Be(2);
        response.Data.Should().HaveCount(1);
    }

    // --- Capability discovery (X1-IMP-003) ---

    [Fact]
    public void OpenAICompatibleProvider_IsIEmbeddingProvider()
    {
        using var handler = new CapturingHttpMessageHandler(ValidEmbeddingResponse);
        var provider = CreateProvider(handler);

        (provider is IEmbeddingProvider).Should().BeTrue();
    }

    [Fact]
    public void ModelCatalogProvider_IsNotIEmbeddingProvider()
    {
        using var handler = new CapturingHttpMessageHandler("{}");
        var catalog = new ProviderCatalog([
            new ProviderDefinition
            {
                Id = "catalog-provider", DisplayName = "Catalog",
                BaseUrl = "https://test.example.com/v1", Protocol = EProviderProtocol.Catalog
            }
        ]);
        var provider = new ModelCatalogProvider(
            new HttpClient(handler),
            new OpenAICompatibleProviderOptions
            {
                BaseUrl = "https://test.example.com/v1",
                ApiKey = "fake-api-key",
                Enabled = true
            },
            catalog,
            "catalog-provider");

        ((object)provider is IEmbeddingProvider).Should().BeFalse();
    }

    [Fact]
    public void MessagesApiProvider_IsNotIEmbeddingProvider()
    {
        using var handler = new CapturingHttpMessageHandler("{}");
        var catalog = new ProviderCatalog([
            new ProviderDefinition
            {
                Id = "messages-provider", DisplayName = "Messages",
                BaseUrl = "https://test.example.com/v1", Protocol = EProviderProtocol.MessagesApi
            }
        ]);
        var provider = new MessagesApiProvider(
            new HttpClient(handler),
            new MessagesApiOptions
            {
                BaseUrl = "https://test.example.com/v1",
                ApiKey = "fake-api-key",
                Enabled = true
            },
            catalog,
            "messages-provider");

        ((object)provider is IEmbeddingProvider).Should().BeFalse();
    }

    [Fact]
    public void KeyQueryProvider_IsNotIEmbeddingProvider()
    {
        using var handler = new CapturingHttpMessageHandler("{}");
        var catalog = new ProviderCatalog([
            new ProviderDefinition
            {
                Id = "keyquery-provider", DisplayName = "KeyQuery",
                BaseUrl = "https://test.example.com/v1", Protocol = EProviderProtocol.KeyQuery
            }
        ]);
        var provider = new KeyQueryProvider(
            new HttpClient(handler),
            new KeyQueryOptions
            {
                BaseUrl = "https://test.example.com/v1",
                ApiKey = "fake-api-key",
                Enabled = true
            },
            catalog,
            "keyquery-provider");

        ((object)provider is IEmbeddingProvider).Should().BeFalse();
    }
}
