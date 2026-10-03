using System.Runtime.CompilerServices;
using System.Text.Json;

using AIProviderConnect.Abstractions;
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
        EnsureProviderEnabled(credentials);
        // Copy with the effective model BEFORE the {model} endpoint template is interpolated and before
        // the model is handed to the parser, so the URL, the body, and the parser name the same model.
        var requestWithModel = request with { Model = ResolveEffectiveModelOrThrow(request.Model, credentials) };
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
            json => KeyQueryWireProtocol.ParseModels(json, Id, Options.ProtocolConfiguration),
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
        EnsureProviderEnabled(credentials);
        var requestWithModel = request with { Model = ResolveEffectiveModelOrThrow(request.Model, credentials) };
        var apiKey = EffectiveApiKey(Options, credentials);

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
        ComposeCredentialHeaderConfigurator(
            credentials,
            ConfigureHeaders,
            request => SetApiKeyHeader(request, _options, EffectiveApiKey(Options, credentials)));
}
