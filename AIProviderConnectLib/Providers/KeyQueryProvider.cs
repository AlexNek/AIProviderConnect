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

    public override Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var endpoint =
            _options.ChatEndpoint.Replace("{model}", Uri.EscapeDataString(request.Model));
        return SendChatAndParseAsync(
            HttpMethod.Post, endpoint, KeyQueryWireProtocol.MapRequest(request), ConfigureHeaders,
            json => KeyQueryWireProtocol.ParseResponse(request.Model, json),
            cancellationToken);
    }

    public override Task<IReadOnlyList<AIModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
        => SendGetModelsAndParseAsync(
            _options.ModelsEndpoint, ConfigureHeaders,
            json => KeyQueryWireProtocol.ParseModels(json, Id),
            cancellationToken);

    public async IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        EnsureProviderEnabled();

        var endpoint =
            _options.StreamEndpoint.Replace("{model}", Uri.EscapeDataString(request.Model)) + "?alt=sse";

        using var httpRequest = BuildRequest(_options, HttpMethod.Post, endpoint, KeyQueryWireProtocol.MapRequest(request), ConfigureHeaders);

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
}
