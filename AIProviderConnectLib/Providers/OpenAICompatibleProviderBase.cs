using System.Runtime.CompilerServices;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIProviderConnect.Providers;

/// <summary>
/// Shared transport for providers using the OpenAI-compatible chat protocol.
/// Endpoint selection, authentication, and catalog parsing remain protocol-specific.
/// </summary>
public abstract class OpenAICompatibleProviderBase : AIProviderBase, IStreamingChatProvider
{
    private readonly string _chatEndpoint;
    private readonly string _modelsEndpoint;
    private readonly string _embeddingsEndpoint;
    private readonly string _defaultEmbeddingModel;

    protected OpenAICompatibleProviderBase(HttpClient httpClient, IProviderCatalog catalog, AIProviderOptions options, string providerId, ILogger? logger = null)
        : this(httpClient, catalog, options, providerId, logger ?? NullLogger.Instance, credentialResolver: null)
    {
    }

    protected OpenAICompatibleProviderBase(
        HttpClient httpClient,
        IProviderCatalog catalog,
        AIProviderOptions options,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
        : base(httpClient, catalog, options, providerId, logger, credentialResolver)
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

    protected virtual void ConfigureHeaders(HttpRequestMessage request, string apiKey) =>
        SetBearerAuthentication(request, apiKey);

    protected virtual void ConfigureHeaders(HttpRequestMessage request) =>
        ConfigureHeaders(request, Options.ApiKey);

    private Action<HttpRequestMessage> BuildHeaderConfigurator(RequestCredentials? credentials) =>
        !string.IsNullOrWhiteSpace(credentials?.ApiKey)
            ? request => ConfigureHeaders(request, EffectiveApiKey(Options, credentials))
            : ConfigureHeaders;

    protected virtual IReadOnlyList<AIModel> ParseModels(JsonElement json) =>
        OpenAICompatibleWireProtocol.ParseModels(json, Id);

    public override async Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request, CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(cancellationToken);
        var requestWithModel = request with { Model = ResolveChatModel(request, credentials) };
        return await SendChatAndParseAsync(
            HttpMethod.Post, ChatEndpoint,
            OpenAICompatibleWireProtocol.MapRequest(requestWithModel, stream: false),
            BuildHeaderConfigurator(credentials),
            OpenAICompatibleWireProtocol.ParseResponse,
            EffectiveBaseUrl(Options, credentials),
            credentials,
            cancellationToken);
    }

    public override async Task<IReadOnlyList<AIModel>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(cancellationToken);
        return await SendGetModelsAndParseAsync(
            ModelsEndpoint,
            configureHeaders: BuildHeaderConfigurator(credentials),
            ParseModels,
            EffectiveBaseUrl(Options, credentials),
            credentials,
            cancellationToken);
    }

    public async IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(cancellationToken);
        var requestWithModel = request with { Model = ResolveChatModel(request, credentials) };
        var apiKey = EffectiveApiKey(Options, credentials);
        EnsureProviderEnabled(credentials);
        using var httpRequest = BuildRequest(
            Options, EffectiveBaseUrl(Options, credentials), apiKey, HttpMethod.Post, ChatEndpoint,
            OpenAICompatibleWireProtocol.MapRequest(requestWithModel, stream: true),
            BuildHeaderConfigurator(credentials));
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

    public async Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(cancellationToken);
        ValidateEmbeddingRequest(request);
        var model = ResolveEmbeddingModel(request.Model, credentials);
        var requestWithModel = request with { Model = model };
        var response = await SendEmbeddingsAndParseAsync(
            EmbeddingsEndpoint,
            OpenAICompatibleWireProtocol.MapEmbeddingsRequest(requestWithModel),
            BuildHeaderConfigurator(credentials),
            OpenAICompatibleWireProtocol.ParseEmbeddingsResponse,
            EffectiveBaseUrl(Options, credentials),
            credentials,
            cancellationToken);

        ValidateEmbeddingResponse(request, response);
        return response;
    }

    // Effective chat model: override → request → DefaultModel, materialized as a copy of the request
    // before the wire mapper runs. Empty resolution is a configuration error naming the provider.
    private string ResolveChatModel(ChatCompletionRequest request, RequestCredentials? credentials)
    {
        var effectiveModel = ResolveEffectiveModel(request.Model, credentials, Options);
        if (string.IsNullOrWhiteSpace(effectiveModel))
            throw new AiException(
                AiErrorCodes.InvalidRequest,
                $"Provider '{Id}' has no model to use for the request. Supply a model in the request, via credentials, or configure a default model.");
        return effectiveModel;
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

    // The wire parser validates each entry in isolation and cannot know how many inputs were
    // sent, so the one-vector-per-input and 0..n-1 ordering invariants are checked here.
    private void ValidateEmbeddingResponse(EmbeddingRequest request, EmbeddingResponse response)
    {
        if (response.Data.Count != request.Input.Count)
        {
            throw new AiException(
                AiErrorCodes.EmbeddingFailed,
                $"Embedding response contains {response.Data.Count} vectors for {request.Input.Count} input strings.");
        }

        for (var position = 0; position < response.Data.Count; position++)
        {
            if (response.Data[position].Index != position)
            {
                throw new AiException(
                    AiErrorCodes.EmbeddingFailed,
                    $"Embedding response is not indexed from 0: the entry at position {position} has index {response.Data[position].Index}.");
            }
        }
    }

    // Embedding model chain: credentials?.Model → request.Model → DefaultEmbeddingModel. The chat
    // DefaultModel is never substituted for an embedding model.
    private string ResolveEmbeddingModel(string requestModel, RequestCredentials? credentials)
    {
        if (!string.IsNullOrWhiteSpace(credentials?.Model))
            return credentials!.Model!;

        if (!string.IsNullOrWhiteSpace(requestModel))
            return requestModel;

        if (!string.IsNullOrWhiteSpace(_defaultEmbeddingModel))
            return _defaultEmbeddingModel;

        throw new AiException(
            AiErrorCodes.EmbeddingModelNotConfigured,
            $"Provider '{Id}' has no embedding model configured. Set DefaultEmbeddingModel in options or provide a model in the request.");
    }
}
