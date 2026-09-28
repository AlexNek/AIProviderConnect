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
/// Concrete provider for Messages API-compatible AI providers.
/// Metadata (Name, DisplayName, etc.) is resolved from the ProviderCatalog at runtime.
/// </summary>
public sealed class MessagesApiProvider : AIProviderBase, IStreamingChatProvider
{
    private readonly MessagesApiOptions _options;

    public MessagesApiProvider(
        HttpClient httpClient,
        MessagesApiOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger? logger = null)
        : base(httpClient, catalog, options, providerId, logger)
    {
        _options = options;
    }

    public MessagesApiProvider(
        HttpClient httpClient,
        MessagesApiOptions options,
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
        var requestWithModel = request with { Model = ResolveChatModel(request, credentials) };
        return await SendChatAndParseAsync(
            HttpMethod.Post, _options.MessagesEndpoint,
            MessagesApiProtocol.MapRequest(requestWithModel), BuildHeaderConfigurator(credentials),
            MessagesApiProtocol.ParseResponse,
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
            json => MessagesApiProtocol.ParseModels(json, Id),
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

        using var httpRequest = BuildRequest(
            _options, EffectiveBaseUrl(Options, credentials), apiKey, HttpMethod.Post, _options.MessagesEndpoint,
            MessagesApiProtocol.MapStreamRequest(requestWithModel), BuildHeaderConfigurator(credentials));

        var parser = new MessagesApiStreamingParser();

        await foreach (var chunk in StreamCoreAsync(
                           httpRequest,
                           item =>
                           {
                               try
                               {
                                   return parser.ParseStreamChunk(
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

    private void ApplyHeaders(HttpRequestMessage request)
        => SetApiKeyHeader(request, _options);

    private Action<HttpRequestMessage> BuildHeaderConfigurator(RequestCredentials? credentials) =>
        !string.IsNullOrWhiteSpace(credentials?.ApiKey)
            ? request => SetApiKeyHeader(request, _options, EffectiveApiKey(Options, credentials))
            : ApplyHeaders;

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
}
