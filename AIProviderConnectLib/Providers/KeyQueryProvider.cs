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
/// Provider for API services that authenticate via a configurable API-key header
/// and follow a model-scoped endpoint pattern (models/{model}:action).
/// </summary>
public sealed class KeyQueryProvider : AIProviderBase, IStreamingChatProvider
{
    private readonly KeyQueryOptions _options;

    public KeyQueryProvider(HttpClient httpClient, KeyQueryOptions options, IProviderCatalog catalog, string providerId, ILogger? logger = null)
        : base(httpClient, catalog, options, providerId, logger)
    {
        _options = options;
    }

    public KeyQueryProvider(
        HttpClient httpClient,
        KeyQueryOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
        : base(httpClient, catalog, options, providerId, logger, credentialResolver)
    {
        _options = options;
    }

    public override async Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(cancellationToken);
        // Copy with the effective model BEFORE the {model} endpoint template is interpolated and before
        // the model is handed to the parser, so the URL, the body, and the parser name the same model.
        var requestWithModel = request with { Model = ResolveChatModel(request, credentials) };
        var endpoint =
            _options.ChatEndpoint.Replace("{model}", Uri.EscapeDataString(requestWithModel.Model));
        return await SendChatAndParseAsync(
            HttpMethod.Post, endpoint, KeyQueryWireProtocol.MapRequest(requestWithModel), BuildHeaderConfigurator(credentials),
            json => KeyQueryWireProtocol.ParseResponse(requestWithModel.Model, json),
            EffectiveBaseUrl(Options, credentials),
            credentials,
            cancellationToken);
    }

    public override async Task<IReadOnlyList<AIModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(cancellationToken);
        return await SendGetModelsAndParseAsync(
            _options.ModelsEndpoint, BuildHeaderConfigurator(credentials),
            json => KeyQueryWireProtocol.ParseModels(json, Id),
            EffectiveBaseUrl(Options, credentials),
            credentials,
            cancellationToken);
    }

    public async IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        var credentials = await ResolveCredentialsAsync(cancellationToken);
        var requestWithModel = request with { Model = ResolveChatModel(request, credentials) };
        var apiKey = EffectiveApiKey(Options, credentials);
        EnsureProviderEnabled(credentials);

        var endpoint =
            _options.StreamEndpoint.Replace("{model}", Uri.EscapeDataString(requestWithModel.Model)) + "?alt=sse";

        using var httpRequest = BuildRequest(
            _options, EffectiveBaseUrl(Options, credentials), apiKey, HttpMethod.Post, endpoint,
            KeyQueryWireProtocol.MapRequest(requestWithModel), BuildHeaderConfigurator(credentials));

        await foreach (var chunk in StreamCoreAsync(
                           httpRequest,
                           item =>
                           {
                               try
                               {
                                   return KeyQueryWireProtocol.ParseStreamChunk(
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

    private void ConfigureHeaders(HttpRequestMessage request)
        => SetApiKeyHeader(request, _options);

    private Action<HttpRequestMessage> BuildHeaderConfigurator(RequestCredentials? credentials) =>
        !string.IsNullOrWhiteSpace(credentials?.ApiKey)
            ? request => SetApiKeyHeader(request, _options, EffectiveApiKey(Options, credentials))
            : ConfigureHeaders;

    // Effective chat model: override → request → DefaultModel, materialized as a copy of the request
    // before the endpoint template and payload mapping consume it. Empty resolution is a configuration
    // error naming the provider.
    private string ResolveChatModel(ChatCompletionRequest request, RequestCredentials? credentials)
    {
        var effectiveModel = ResolveEffectiveModel(request.Model, credentials, Options);
        if (string.IsNullOrWhiteSpace(effectiveModel))
            throw new AiException(
                AiErrorCodes.InvalidRequest,
                $"Provider '{Id}' has no model to use for the request. Supply a model in the request, via credentials, or configure a default model.");
        return effectiveModel;
    }
}
