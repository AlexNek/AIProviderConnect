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

    public override Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
        => SendChatAndParseAsync(
            HttpMethod.Post, _options.MessagesEndpoint,
            MessagesApiProtocol.MapRequest(request), ApplyHeaders,
            MessagesApiProtocol.ParseResponse,
            cancellationToken);

    public override Task<IReadOnlyList<AIModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
        => SendGetModelsAndParseAsync(
            _options.ModelsEndpoint, ApplyHeaders,
            json => MessagesApiProtocol.ParseModels(json, Id),
            cancellationToken);

    public async IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        EnsureProviderEnabled();

        using var httpRequest = BuildRequest(_options, HttpMethod.Post, _options.MessagesEndpoint,
            MessagesApiProtocol.MapStreamRequest(request), ApplyHeaders);

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
}
