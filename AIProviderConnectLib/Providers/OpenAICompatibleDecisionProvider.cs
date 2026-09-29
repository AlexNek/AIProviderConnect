using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIProviderConnect.Providers;

/// <summary>
/// Concrete provider for an OpenAI-compatible or hybrid-gateway primary that also declares a
/// decisions override in its <c>endpoints</c> block. Chat, streaming, embeddings, and model
/// discovery come from <see cref="OpenAICompatibleProviderBase"/> unchanged; the only added code
/// is the decisions transport. Selected by <c>RegisterProvider</c> when the definition declares
/// <c>endpoints["decisions"].protocol = "decision"</c>.
/// </summary>
public sealed class OpenAICompatibleDecisionProvider
    : OpenAICompatibleProviderBase, IEmbeddingProvider, IDecisionProvider
{
    private readonly string _decisionsEndpoint;
    private readonly string? _decisionsBaseUrl;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAICompatibleDecisionProvider"/> class.
    /// </summary>
    public OpenAICompatibleDecisionProvider(
        HttpClient httpClient,
        AIProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger? logger = null)
        : this(httpClient, options, catalog, providerId, logger ?? NullLogger.Instance, credentialResolver: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAICompatibleDecisionProvider"/> class.
    /// </summary>
    public OpenAICompatibleDecisionProvider(
        HttpClient httpClient,
        AIProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
        : base(httpClient, catalog, options, providerId, logger, credentialResolver)
    {
        var decisionOptions = options as IDecisionsEndpointOptions
            ?? throw new ArgumentException(
                $"Options type '{options.GetType().Name}' does not implement IDecisionsEndpointOptions. " +
                "OpenAICompatibleDecisionProvider requires options with DecisionsEndpoint and DecisionsBaseUrl.",
                nameof(options));
        _decisionsEndpoint = decisionOptions.DecisionsEndpoint;
        _decisionsBaseUrl = decisionOptions.DecisionsBaseUrl;
    }

    /// <inheritdoc />
    public bool SupportsDecisions => true;

    /// <inheritdoc />
    public async Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credentials = await ResolveCredentialsAsync(cancellationToken);
        EnsureProviderEnabled(credentials);

        var effectiveModel = ResolveEffectiveModel(request.Model, credentials, Options);
        if (string.IsNullOrWhiteSpace(effectiveModel))
            throw new AiException(
                AiErrorCodes.InvalidRequest,
                $"Provider '{Id}' has no model to use for the request. Supply a model in the request, via credentials, or configure a default model.");

        var requestWithModel = request with { Model = effectiveModel };

        return await SendDecisionAndParseAsync(
            _decisionsEndpoint,
            DecisionsWireProtocol.MapRequest(requestWithModel),
            BuildHeaderConfigurator(credentials),
            DecisionsWireProtocol.ParseResponse,
            ResolveDecisionsBaseUrl(credentials),
            credentials,
            cancellationToken);
    }

    // The decisions surface may live on a different root than the provider BaseUrl (the
    // endpoints["decisions"].baseUrl override). When the override is absent the effective
    // base URL (per-request or configured) is used. When an API key is present, the resolved
    // URL must be HTTPS to protect credentials in transit.
    private string ResolveDecisionsBaseUrl(RequestCredentials? credentials)
    {
        var resolvedUrl = !string.IsNullOrWhiteSpace(_decisionsBaseUrl)
            ? _decisionsBaseUrl!
            : EffectiveBaseUrl(Options, credentials);

        if (string.IsNullOrWhiteSpace(resolvedUrl))
            throw new AiException(AiErrorCodes.NoBaseUrl, $"Provider '{Id}' is missing a decisions base URL.");

        var effectiveApiKey = EffectiveApiKey(Options, credentials);
        if (!string.IsNullOrWhiteSpace(effectiveApiKey)
            && Uri.TryCreate(resolvedUrl, UriKind.Absolute, out var uri)
            && !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            throw new AiException(AiErrorCodes.InvalidRequest,
                $"Provider '{Id}': decisions base URL must use HTTPS when an API key is present.");
        }

        return resolvedUrl;
    }

    // Delegates to the base virtual ConfigureHeaders so an overridden auth scheme is honored
    // exactly as for chat.
    private Action<HttpRequestMessage> BuildHeaderConfigurator(RequestCredentials? credentials) =>
        !string.IsNullOrWhiteSpace(credentials?.ApiKey)
            ? request => ConfigureHeaders(request, EffectiveApiKey(Options, credentials))
            : ConfigureHeaders;
}
