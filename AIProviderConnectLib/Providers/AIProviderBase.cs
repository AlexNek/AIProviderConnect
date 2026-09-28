using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Constants;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.Retry;

namespace AIProviderConnect.Providers;

/// <summary>
/// Abstract base class for AI providers.
/// Implements the core <see cref="IAIProvider"/> runtime interface.
/// Optional capabilities (<see cref="IStreamingChatProvider"/>, <see cref="IModelDiscoveryProvider"/>)
/// are implemented by concrete providers.
/// </summary>
public abstract class AIProviderBase : IAIProvider, IModelDiscoveryProvider
{
    /// <inheritdoc />
    public string Id => Definition.Id;

    /// <inheritdoc />
    public bool IsEnabled => Options.Enabled;

    /// <inheritdoc />
    public EProviderProtocol Protocol => Definition.Protocol;

    /// <summary>
    /// Indicates whether this provider requires an API key to operate.
    /// Override to false for providers that work without authentication.
    /// </summary>
    public virtual bool RequiresApiKey => true;

    /// <inheritdoc />
    public virtual bool SupportsModelDiscovery => Definition.HasModelDiscoveryApi;

    /// <summary>
    /// Gets the definition resolved during construction.
    /// </summary>
    protected ProviderDefinition Definition { get; }

    /// <summary>
    /// Gets the resolved options for this provider, including API key, base URL, and default headers.
    /// </summary>
    protected AIProviderOptions Options { get; }

    /// <summary>
    /// Gets the HTTP client used for provider requests.
    /// </summary>
    protected HttpClient HttpClient { get; }

    /// <summary>
    /// Gets the provider identifier used to look up metadata from the provider catalog.
    /// </summary>
    protected string ProviderId { get; }

    /// <summary>
    /// Gets the logger for diagnostic output. Defaults to <see cref="NullLogger.Instance"/>.
    /// </summary>
    protected ILogger Logger { get; }

    private readonly ResiliencePipeline _resiliencePipeline;
    private readonly ICredentialResolver? _credentialResolver;
    private RequestCredentials? _fixedCredentials;
    private bool _fixedCredentialsSet;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProviderBase"/> class using the configured
    /// credential source only (no per-call <see cref="ICredentialResolver"/>).
    /// </summary>
    protected AIProviderBase(HttpClient httpClient, IProviderCatalog catalog, AIProviderOptions options, string providerId, ILogger? logger = null)
        : this(httpClient, catalog, options, providerId, logger ?? NullLogger.Instance, credentialResolver: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AIProviderBase"/> class, declaring every parameter
    /// as required so DI construction can pass a consumer-supplied <see cref="ICredentialResolver"/>
    /// without introducing an overload-resolution ambiguity for calls that omit both logger and resolver.
    /// </summary>
    protected AIProviderBase(
        HttpClient httpClient,
        IProviderCatalog catalog,
        AIProviderOptions options,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(providerId);

        HttpClient = httpClient;
        Options = options;
        ProviderId = providerId;
        Logger = logger ?? NullLogger.Instance;
        _credentialResolver = credentialResolver;
        Definition = catalog.Get(providerId)
            ?? throw new InvalidOperationException($"Provider '{providerId}' not found in catalog.");
        _resiliencePipeline = BuildResiliencePipeline();
    }

    /// <summary>
    /// Gets or sets credential overrides attached once to an activator-created transient instance before
    /// publication. The backing field accepts its first write and throws <see cref="InvalidOperationException"/>
    /// on any later write, so the single-assignment rule is enforced rather than assumed. Never set on the
    /// DI singleton, which is shared by every concurrent caller.
    /// </summary>
    internal RequestCredentials? FixedCredentials
    {
        get => _fixedCredentials;
        set
        {
            if (_fixedCredentialsSet)
                throw new InvalidOperationException(
                    $"Provider '{ProviderId}': fixed credentials have already been assigned and cannot be overwritten.");
            _fixedCredentials = value;
            _fixedCredentialsSet = true;
        }
    }

    private ResiliencePipeline BuildResiliencePipeline()
    {
        if (Options.MaxRetryCount <= 0)
            return ResiliencePipeline.Empty;

        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<AiException>(ex =>
                    ex.Code == AiErrorCodes.RateLimited || ex.Code == AiErrorCodes.NoServer
                    || ex.Code == AiErrorCodes.NoConnection || ex.Code == AiErrorCodes.Timeout),
                MaxRetryAttempts = Options.MaxRetryCount,
                Delay = Options.RetryDelay,
                BackoffType = DelayBackoffType.Exponential,
                OnRetry = args =>
                {
                    Logger.LogWarning(
                        "Provider '{ProviderId}': retry {Attempt}/{MaxAttempts} after {Delay}ms — {Code}: {Message}",
                        ProviderId, args.AttemptNumber + 1, Options.MaxRetryCount,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception is AiException aiEx ? aiEx.Code : "unknown",
                        args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .Build();
    }

    /// <inheritdoc />
    public abstract Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Default implementation of <see cref="IModelDiscoveryProvider.GetModelsAsync"/>.
    /// Throws <see cref="AiException"/> with <see cref="AiErrorCodes.ModelDiscoveryNotSupported"/>
    /// unless overridden by a concrete provider that supports model discovery.
    /// </summary>
    public virtual Task<IReadOnlyList<AIModel>>
        GetModelsAsync(CancellationToken cancellationToken) =>
        throw new AiException(
            AiErrorCodes.ModelDiscoveryNotSupported,
            $"Provider '{Id}' does not support model discovery.");

    protected void EnsureProviderEnabled()
    {
        if (!Options.Enabled)
            throw new AiException(AiErrorCodes.ProviderDisabled, $"Provider '{Id}' is disabled.");
        if (string.IsNullOrWhiteSpace(Options.BaseUrl))
            throw new AiException(AiErrorCodes.NoBaseUrl, $"Provider '{Id}' is missing a base URL.");
        if (RequiresApiKey && string.IsNullOrWhiteSpace(Options.ApiKey))
            throw new AiException(AiErrorCodes.NoApiKey, $"Provider '{Id}' is missing an API key.");
    }

    /// <summary>
    /// Validates the <em>effective</em> configuration for a call that may carry per-request
    /// <paramref name="credentials"/>: <see cref="AIProviderOptions.Enabled"/>, the effective base URL,
    /// and — when <see cref="RequiresApiKey"/> is true — the effective API key. A provider configured
    /// with an empty key can complete a call once an override supplies one.
    /// </summary>
    protected void EnsureProviderEnabled(RequestCredentials? credentials)
    {
        if (!Options.Enabled)
            throw new AiException(AiErrorCodes.ProviderDisabled, $"Provider '{Id}' is disabled.");
        if (string.IsNullOrWhiteSpace(EffectiveBaseUrl(Options, credentials)))
            throw new AiException(AiErrorCodes.NoBaseUrl, $"Provider '{Id}' is missing a base URL.");
        if (RequiresApiKey && string.IsNullOrWhiteSpace(EffectiveApiKey(Options, credentials)))
            throw new AiException(AiErrorCodes.NoApiKey, $"Provider '{Id}' is missing an API key.");
    }

    /// <summary>
    /// Resolves the per-call credential overrides once at the call site. Returns
    /// <see cref="FixedCredentials"/> when set (factory-overload path), otherwise consults the injected
    /// <see cref="ICredentialResolver"/> (normalizing an all-unset record to <c>null</c>), otherwise
    /// <c>null</c> (use configured values). A throwing resolver surfaces as
    /// <see cref="AiException"/> with <see cref="AiErrorCodes.ProviderMissingConfiguration"/>; caller
    /// cancellation propagates unchanged.
    /// </summary>
    protected virtual async ValueTask<RequestCredentials?> ResolveCredentialsAsync(
        CancellationToken cancellationToken)
    {
        if (FixedCredentials is { } fixedCredentials)
            return fixedCredentials;

        if (_credentialResolver is null)
            return null;

        try
        {
            var resolved = await _credentialResolver.ResolveAsync(ProviderId, cancellationToken);
            return IsAllUnset(resolved) ? null : resolved;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new AiException(
                AiErrorCodes.ProviderMissingConfiguration,
                $"Credential resolution for provider '{Id}' failed.",
                ex);
        }
    }

    /// <summary>
    /// Returns the effective base URL: <see cref="RequestCredentials.BaseUrl"/> when non-null and
    /// non-whitespace, otherwise <see cref="AIProviderOptions.BaseUrl"/>. A plain <c>??</c> chain is not
    /// sufficient because an empty string from a resolver is non-null; null, empty, and whitespace all
    /// fall back to the configured value.
    /// </summary>
    protected static string EffectiveBaseUrl(AIProviderOptions options, RequestCredentials? credentials) =>
        !string.IsNullOrWhiteSpace(credentials?.BaseUrl) ? credentials!.BaseUrl! : options.BaseUrl;

    /// <summary>
    /// Returns the effective API key: <see cref="RequestCredentials.ApiKey"/> when non-null and
    /// non-whitespace, otherwise <see cref="AIProviderOptions.ApiKey"/>. Null, empty, and whitespace all
    /// fall back to the configured value.
    /// </summary>
    protected static string EffectiveApiKey(AIProviderOptions options, RequestCredentials? credentials) =>
        !string.IsNullOrWhiteSpace(credentials?.ApiKey) ? credentials!.ApiKey! : options.ApiKey;

    /// <summary>
    /// Resolves the effective model for a chat request: <see cref="RequestCredentials.Model"/> when
    /// non-empty, else the request's model when non-empty, else
    /// <see cref="AIProviderOptions.DefaultModel"/> when non-empty. The result may be empty; the caller
    /// throws <see cref="AiException"/> with <see cref="AiErrorCodes.InvalidRequest"/> in that case.
    /// </summary>
    protected static string ResolveEffectiveModel(
        string requestModel,
        RequestCredentials? credentials,
        AIProviderOptions options)
    {
        if (!string.IsNullOrWhiteSpace(credentials?.Model))
            return credentials!.Model!;
        if (!string.IsNullOrWhiteSpace(requestModel))
            return requestModel;
        return options.DefaultModel;
    }

    private static bool IsAllUnset(RequestCredentials? credentials) =>
        credentials is null
        || (string.IsNullOrWhiteSpace(credentials.ApiKey)
            && string.IsNullOrWhiteSpace(credentials.BaseUrl)
            && string.IsNullOrWhiteSpace(credentials.Model));

    /// <summary>
    /// Builds a request using protocol-specific headers, or Bearer authentication when
    /// no header configurator is supplied. Shared clients are never mutated.
    /// Delegates to the effective-value overload with the configured base URL and key.
    /// </summary>
    protected static HttpRequestMessage BuildRequest(
        AIProviderOptions options,
        HttpMethod method,
        string endpoint,
        object? body = null,
        Action<HttpRequestMessage>? configureHeaders = null) =>
        BuildRequest(options, options.BaseUrl, options.ApiKey, method, endpoint, body, configureHeaders);

    /// <summary>
    /// Builds a request from an explicit effective <paramref name="baseUrl"/> and
    /// <paramref name="apiKey"/>, so a per-call override reaches the outgoing request without mutating
    /// the shared <see cref="AIProviderOptions"/>. The configured <paramref name="options"/> still supply
    /// the default headers.
    /// </summary>
    protected static HttpRequestMessage BuildRequest(
        AIProviderOptions options,
        string baseUrl,
        string apiKey,
        HttpMethod method,
        string endpoint,
        object? body = null,
        Action<HttpRequestMessage>? configureHeaders = null)
    {
        var url = new Uri(new Uri(EnsureTrailingSlash(baseUrl)), endpoint);
        var request = new HttpRequestMessage(method, url);
        try
        {
            if (configureHeaders is null)
                SetBearerAuthentication(request, apiKey);
            else
                configureHeaders(request);

            foreach (var header in options.DefaultHeaders)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            if (body is not null)
                request.Content = JsonContent.Create(body);

            return request;
        }
        catch
        {
            request.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Ensures the base URL ends with a trailing slash.
    /// </summary>
    protected static string EnsureTrailingSlash(string baseUrl) =>
        baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/";

    /// <summary>
    /// Reads server-sent events from a streaming response.
    /// </summary>
    protected static async IAsyncEnumerable<string> ReadServerSentEventsAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(
                    SSEConstants.DataPrefix,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[SSEConstants.DataPrefix.Length..].Trim();
            if (string.Equals(data, SSEConstants.DoneSentinel, StringComparison.Ordinal))
            {
                yield break;
            }

            yield return data;
        }
    }

    /// <summary>
    /// Shared streaming transport: sends the request with <see cref="HttpCompletionOption.ResponseHeadersRead"/>,
    /// validates the response, reads SSE frames, and yields parsed chunks.
    /// Each provider builds its own <see cref="HttpRequestMessage"/> (endpoint, body, headers)
    /// and delegates the transport to this method.
    /// </summary>
    protected async IAsyncEnumerable<StreamingChatChunk> StreamCoreAsync(
        HttpRequestMessage httpRequest,
        Func<string, StreamingChatChunk?> chunkParser,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await TranslateNetworkExceptionsAsync(
            () => HttpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken),
            cancellationToken);
        await ThrowIfErrorAsync(response, cancellationToken);

        await foreach (var item in ReadServerSentEventsAsync(response, cancellationToken))
        {
            var chunk = chunkParser(item);
            if (chunk is not null)
            {
                yield return chunk;
            }
        }
    }

    /// <summary>
    /// Wraps <paramref name="send"/> to translate network-level exceptions into <see cref="AiException"/>:
    /// <see cref="HttpRequestException"/> → <see cref="AiErrorCodes.NoConnection"/>,
    /// timeout-type <see cref="TaskCanceledException"/> → <see cref="AiErrorCodes.Timeout"/>.
    /// User-initiated cancellation (where <paramref name="cancellationToken"/> is cancelled) propagates
    /// as <see cref="OperationCanceledException"/> unchanged.
    /// </summary>
    private async Task<HttpResponseMessage> TranslateNetworkExceptionsAsync(
        Func<Task<HttpResponseMessage>> send,
        CancellationToken cancellationToken)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException ex)
        {
            throw new AiException(AiErrorCodes.NoConnection, ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiException(AiErrorCodes.Timeout, ex.Message, ex);
        }
    }

    /// <summary>
    /// Shared non-streaming chat transport: builds the request, sends it, validates the response,
    /// deserializes the JSON body, and delegates parsing to the caller-supplied function.
    /// Delegates to the effective-value overload with the configured base URL and no override.
    /// </summary>
    protected Task<ChatCompletionResponse> SendChatAndParseAsync(
        HttpMethod method,
        string endpoint,
        object? payload,
        Action<HttpRequestMessage>? configureHeaders,
        Func<JsonElement, ChatCompletionResponse> parseResponse,
        CancellationToken cancellationToken) =>
        SendChatAndParseAsync(
            method, endpoint, payload, configureHeaders, parseResponse,
            Options.BaseUrl, null, cancellationToken);

    /// <summary>
    /// Shared non-streaming chat transport carrying the effective base URL and the per-request header
    /// callback, validating the effective configuration. A retry reuses the <paramref name="credentials"/>
    /// resolved for that call — the pipeline never re-consults the resolver.
    /// </summary>
    protected async Task<ChatCompletionResponse> SendChatAndParseAsync(
        HttpMethod method,
        string endpoint,
        object? payload,
        Action<HttpRequestMessage>? configureHeaders,
        Func<JsonElement, ChatCompletionResponse> parseResponse,
        string baseUrl,
        RequestCredentials? credentials,
        CancellationToken cancellationToken)
    {
        EnsureProviderEnabled(credentials);
        Logger.LogDebug("Provider '{ProviderId}': sending {Method} request to {Endpoint}", ProviderId, method, endpoint);
        try
        {
            return await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                using var httpRequest = BuildRequest(
                    Options, baseUrl, EffectiveApiKey(Options, credentials), method, endpoint, payload, configureHeaders);
                using var response = await TranslateNetworkExceptionsAsync(
                    () => HttpClient.SendAsync(httpRequest, ct), ct);
                await ThrowIfErrorAsync(response, ct);
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                Logger.LogDebug("Provider '{ProviderId}': response received (HTTP {StatusCode})", ProviderId, (int)response.StatusCode);
                return parseResponse(json);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTransportFailure(ex, endpoint);
            throw;
        }
    }

    /// <summary>
    /// Shared model-discovery transport: builds a GET request, sends it, validates the response,
    /// deserializes the JSON body, and delegates parsing to the caller-supplied function.
    /// Delegates to the effective-value overload with the configured base URL and no override.
    /// </summary>
    protected Task<IReadOnlyList<AIModel>> SendGetModelsAndParseAsync(
        string endpoint,
        Action<HttpRequestMessage>? configureHeaders,
        Func<JsonElement, IReadOnlyList<AIModel>> parseModels,
        CancellationToken cancellationToken) =>
        SendGetModelsAndParseAsync(
            endpoint, configureHeaders, parseModels,
            Options.BaseUrl, null, cancellationToken);

    /// <summary>
    /// Shared model-discovery transport carrying the effective base URL and the per-request header
    /// callback, validating the effective configuration.
    /// </summary>
    protected async Task<IReadOnlyList<AIModel>> SendGetModelsAndParseAsync(
        string endpoint,
        Action<HttpRequestMessage>? configureHeaders,
        Func<JsonElement, IReadOnlyList<AIModel>> parseModels,
        string baseUrl,
        RequestCredentials? credentials,
        CancellationToken cancellationToken)
    {
        EnsureProviderEnabled(credentials);
        Logger.LogDebug("Provider '{ProviderId}': sending GET request to {Endpoint}", ProviderId, endpoint);
        try
        {
            return await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                using var httpRequest = BuildRequest(
                    Options, baseUrl, EffectiveApiKey(Options, credentials), HttpMethod.Get, endpoint,
                    configureHeaders: configureHeaders);
                using var response = await TranslateNetworkExceptionsAsync(
                    () => HttpClient.SendAsync(httpRequest, ct), ct);
                await ThrowIfErrorAsync(response, ct);
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                var models = parseModels(json);
                Logger.LogDebug("Provider '{ProviderId}': model discovery returned {Count} models", ProviderId, models.Count);
                return models;
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTransportFailure(ex, endpoint);
            throw;
        }
    }

    /// <summary>
    /// Shared embeddings transport: builds a POST request, sends it, validates the response,
    /// deserializes the JSON body, and delegates parsing to the caller-supplied function.
    /// Delegates to the effective-value overload with the configured base URL and no override.
    /// </summary>
    protected Task<EmbeddingResponse> SendEmbeddingsAndParseAsync(
        string endpoint,
        object? payload,
        Action<HttpRequestMessage>? configureHeaders,
        Func<JsonElement, EmbeddingResponse> parseResponse,
        CancellationToken cancellationToken) =>
        SendEmbeddingsAndParseAsync(
            endpoint, payload, configureHeaders, parseResponse,
            Options.BaseUrl, null, cancellationToken);

    /// <summary>
    /// Shared embeddings transport carrying the effective base URL and the per-request header callback,
    /// validating the effective configuration.
    /// </summary>
    protected async Task<EmbeddingResponse> SendEmbeddingsAndParseAsync(
        string endpoint,
        object? payload,
        Action<HttpRequestMessage>? configureHeaders,
        Func<JsonElement, EmbeddingResponse> parseResponse,
        string baseUrl,
        RequestCredentials? credentials,
        CancellationToken cancellationToken)
    {
        EnsureProviderEnabled(credentials);
        Logger.LogDebug("Provider '{ProviderId}': sending POST request to {Endpoint}", ProviderId, endpoint);
        try
        {
            return await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                using var httpRequest = BuildRequest(
                    Options, baseUrl, EffectiveApiKey(Options, credentials), HttpMethod.Post, endpoint, payload, configureHeaders);
                using var response = await TranslateNetworkExceptionsAsync(
                    () => HttpClient.SendAsync(httpRequest, ct), ct);
                await ThrowIfEmbeddingErrorAsync(response, ct);
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                Logger.LogDebug("Provider '{ProviderId}': embeddings response received (HTTP {StatusCode})", ProviderId, (int)response.StatusCode);
                return parseResponse(json);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTransportFailure(ex, endpoint);
            throw;
        }
    }

    private void LogTransportFailure(Exception ex, string endpoint)
    {
        if (ex is AiException { Code: AiErrorCodes.RateLimited })
        {
            Logger.LogWarning(ex, "Provider '{ProviderId}': request to {Endpoint} was rate-limited", ProviderId, endpoint);
            return;
        }

        Logger.LogError(ex, "Provider '{ProviderId}': request to {Endpoint} failed", ProviderId, endpoint);
    }

    /// <summary>
    /// Configures Bearer token authentication for a single request.
    /// </summary>
    protected static void SetBearerAuthentication(HttpRequestMessage request, string apiKey)
    {
        const string bearerScheme = "Bearer";
        var trimmed = apiKey?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(bearerScheme, trimmed);
        }
    }

    /// <summary>
    /// Configures API-key authentication via a custom header name for a single request, using the key
    /// from <paramref name="options"/>. Delegates to the effective-key overload.
    /// </summary>
    protected void SetApiKeyHeader(HttpRequestMessage request, AIProviderOptions options) =>
        SetApiKeyHeader(request, options, options.ApiKey);

    /// <summary>
    /// Configures API-key authentication via a custom header name for a single request using an explicit
    /// effective <paramref name="apiKey"/>, so a per-call override reaches the outgoing request. The header
    /// name is still read from <paramref name="options"/>. Throws
    /// <see cref="InvalidOperationException"/> when the key is present but
    /// <see cref="AIProviderOptions.CustomAuthHeaderName"/> is not set.
    /// </summary>
    protected void SetApiKeyHeader(HttpRequestMessage request, AIProviderOptions options, string apiKey)
    {
        var trimmedKey = apiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedKey))
        {
            var headerName = options.CustomAuthHeaderName
                ?? throw new InvalidOperationException(
                    $"Provider '{ProviderId}' uses API-key auth but CustomAuthHeaderName is not set.");
            request.Headers.TryAddWithoutValidation(headerName, trimmedKey);
        }
    }

    /// <summary>
    /// Throws an exception if the response indicates an error.
    /// </summary>
    protected static async Task ThrowIfErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await BuildHttpErrorMessageAsync(response, cancellationToken);
        var code = MapHttpStatusToErrorCode((int)response.StatusCode);
        throw new AiException(code, message);
    }

    /// <summary>
    /// Embeddings-specific error check: maps HTTP 400, 404, and 422 to
    /// <see cref="AiErrorCodes.EmbeddingFailed"/> while preserving retry-triggering
    /// classifications (429 → <see cref="AiErrorCodes.RateLimited"/>,
    /// 5xx → <see cref="AiErrorCodes.NoServer"/>).
    /// </summary>
    private static async Task ThrowIfEmbeddingErrorAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var message = await BuildHttpErrorMessageAsync(response, cancellationToken);
        var statusCode = (int)response.StatusCode;
        var code = statusCode switch
        {
            400 or 404 or 422 => AiErrorCodes.EmbeddingFailed,
            _ => MapHttpStatusToErrorCode(statusCode)
        };
        throw new AiException(code, message);
    }

    private static string MapHttpStatusToErrorCode(int statusCode) => statusCode switch
    {
        400 => AiErrorCodes.InvalidRequest,
        429 => AiErrorCodes.RateLimited,
        401 => AiErrorCodes.Unauthorized,
        403 => AiErrorCodes.Forbidden,
        404 => AiErrorCodes.EndpointNotFound,
        >= 500 => AiErrorCodes.NoServer,
        _ => AiErrorCodes.ProviderCallFailed
    };

    private static async Task<string> BuildHttpErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var statusCode = (int)response.StatusCode;
        var message = $"HTTP {statusCode}: {HttpErrorMessages.GetStatusMessage(statusCode)}";
        var detail = TryExtractJsonMessage(body);
        if (!string.IsNullOrWhiteSpace(detail))
            message = $"{message} — {detail}";
        return message;
    }

    private static readonly string[] ErrorMessagePaths = ["error.message", "message"];

    private static string? TryExtractJsonMessage(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var path in ErrorMessagePaths)
            {
                var element = doc.RootElement;
                var segments = path.Split('.');
                var found = true;
                foreach (var segment in segments)
                {
                    if (!element.TryGetProperty(segment, out element))
                    {
                        found = false;
                        break;
                    }
                }

                if (found && element.ValueKind == JsonValueKind.String)
                {
                    var text = element.GetString() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
