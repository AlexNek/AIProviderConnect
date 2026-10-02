using AIProviderConnect.Abstractions;
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

        var requestWithModel = request with { Model = ResolveEffectiveModelOrThrow(request.Model, credentials) };

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
    // endpoints["decisions"].baseUrl override); resolution, precedence, and safety validation are
    // shared with the other surfaces via ResolveSurfaceBaseUrl. The inherited BuildHeaderConfigurator
    // from OpenAICompatibleProviderBase already honors the virtual ConfigureHeaders for chat.
    private string ResolveDecisionsBaseUrl(RequestCredentials? credentials) =>
        ResolveSurfaceBaseUrl(_decisionsBaseUrl, "decisions", credentials);
}
