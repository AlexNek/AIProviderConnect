using System.Runtime.CompilerServices;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using Microsoft.Extensions.Logging;

namespace AIProviderConnect.Providers;

/// <summary>
/// Shared transport for providers using the OpenAI-compatible chat protocol.
/// Endpoint selection, authentication, and catalog parsing remain protocol-specific.
/// </summary>
public abstract class OpenAICompatibleProviderBase : AIProviderBase, IStreamingChatProvider
{
    private readonly string _chatEndpoint;
    private readonly string _modelsEndpoint;

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
    }

    protected string ChatEndpoint => _chatEndpoint;
    protected string ModelsEndpoint => _modelsEndpoint;

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
}
