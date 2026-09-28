using System.Runtime.CompilerServices;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using Microsoft.Extensions.Logging;

namespace AIProviderConnect.Providers;

/// <summary>
/// Shared transport for providers using the OpenAI-compatible chat protocol.
/// Endpoint selection, authentication, and catalog parsing remain protocol-specific.
/// </summary>
public abstract class OpenAICompatibleProviderBase : AIProviderBase, IStreamingChatProvider, IEmbeddingProvider
{
    private readonly string _chatEndpoint;
    private readonly string _modelsEndpoint;
    private readonly string _embeddingsEndpoint;
    private readonly string _defaultEmbeddingModel;

    protected OpenAICompatibleProviderBase(HttpClient httpClient, IProviderCatalog catalog, AIProviderOptions options, string providerId, ILogger? logger = null)
        : base(httpClient, catalog, options, providerId, logger)
    {
        var endpointOptions = options as IChatAndModelsEndpointOptions
            ?? throw new ArgumentException(
                $"Options type '{options.GetType().Name}' does not implement IChatAndModelsEndpointOptions. " +
                "OpenAICompatibleProviderBase requires options with ChatEndpoint and ModelsEndpoint.",
                nameof(options));
        _chatEndpoint = endpointOptions.ChatEndpoint;
        _modelsEndpoint = endpointOptions.ModelsEndpoint;

        var embeddingsOptions = options as IEmbeddingsEndpointOptions
            ?? throw new ArgumentException(
                $"Options type '{options.GetType().Name}' does not implement IEmbeddingsEndpointOptions. " +
                "OpenAICompatibleProviderBase requires options with EmbeddingsEndpoint and DefaultEmbeddingModel.",
                nameof(options));
        _embeddingsEndpoint = embeddingsOptions.EmbeddingsEndpoint;
        _defaultEmbeddingModel = embeddingsOptions.DefaultEmbeddingModel;
    }

    protected string ChatEndpoint => _chatEndpoint;
    protected string ModelsEndpoint => _modelsEndpoint;
    protected string EmbeddingsEndpoint => _embeddingsEndpoint;
    protected string DefaultEmbeddingModel => _defaultEmbeddingModel;

    protected virtual void ConfigureHeaders(HttpRequestMessage request) =>
        SetBearerAuthentication(request, Options.ApiKey);

    protected virtual IReadOnlyList<AIModel> ParseModels(JsonElement json) =>
        OpenAICompatibleWireProtocol.ParseModels(json, Id);

    public override Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default) =>
        SendChatAndParseAsync(
            HttpMethod.Post, ChatEndpoint,
            OpenAICompatibleWireProtocol.MapRequest(request, stream: false),
            ConfigureHeaders,
            OpenAICompatibleWireProtocol.ParseResponse,
            cancellationToken);

    public override Task<IReadOnlyList<AIModel>> GetModelsAsync(CancellationToken cancellationToken = default) =>
        SendGetModelsAndParseAsync(
            ModelsEndpoint,
            configureHeaders: ConfigureHeaders,
            ParseModels,
            cancellationToken);

    public async IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        EnsureProviderEnabled();
        using var httpRequest = BuildRequest(Options, HttpMethod.Post, ChatEndpoint,
            OpenAICompatibleWireProtocol.MapRequest(request, stream: true), ConfigureHeaders);
        await foreach (var chunk in StreamCoreAsync(
                           httpRequest,
                           item =>
                           {
                               try
                               {
                                   return OpenAICompatibleWireProtocol.ParseStreamChunk(
                                       JsonSerializer.Deserialize<JsonElement>(item));
                               }
                               catch (JsonException ex)
                               {
                                   Logger.LogWarning(ex, "Provider '{ProviderId}': skipping malformed SSE data", ProviderId);
                                   return null;
                               }
                           },
                           cancellationToken))
        {
            yield return chunk;
        }
    }

    public Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEmbeddingRequest(request);
        var model = ResolveEmbeddingModel(request.Model);
        var requestWithModel = request with { Model = model };
        return SendEmbeddingsAndParseAsync(
            EmbeddingsEndpoint,
            OpenAICompatibleWireProtocol.MapEmbeddingsRequest(requestWithModel),
            ConfigureHeaders,
            OpenAICompatibleWireProtocol.ParseEmbeddingsResponse,
            cancellationToken);
    }

    private void ValidateEmbeddingRequest(EmbeddingRequest request)
    {
        if (request.Input is null || request.Input.Count == 0)
        {
            throw new AiException(
                AiErrorCodes.InvalidRequest,
                "Embedding request input cannot be empty.");
        }

        foreach (var input in request.Input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                throw new AiException(
                    AiErrorCodes.InvalidRequest,
                    "Embedding request input cannot contain empty or whitespace strings.");
            }
        }
    }

    private string ResolveEmbeddingModel(string requestModel)
    {
        if (!string.IsNullOrWhiteSpace(requestModel))
            return requestModel;

        if (!string.IsNullOrWhiteSpace(_defaultEmbeddingModel))
            return _defaultEmbeddingModel;

        throw new AiException(
            AiErrorCodes.EmbeddingModelNotConfigured,
            $"Provider '{Id}' has no embedding model configured. Set DefaultEmbeddingModel in options or provide a model in the request.");
    }
}
