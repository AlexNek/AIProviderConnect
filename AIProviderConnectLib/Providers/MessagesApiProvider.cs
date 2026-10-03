using System.Runtime.CompilerServices;
using System.Text.Json;

using AIProviderConnect.Abstractions;
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
        EnsureProviderEnabled(credentials);
        var requestWithModel = request with { Model = ResolveEffectiveModelOrThrow(request.Model, credentials) };
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
            json => MessagesApiProtocol.ParseModels(json, Id, Options.ProtocolConfiguration),
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
        ComposeCredentialHeaderConfigurator(
            credentials,
            ApplyHeaders,
            request => SetApiKeyHeader(request, _options, EffectiveApiKey(Options, credentials)));
}
