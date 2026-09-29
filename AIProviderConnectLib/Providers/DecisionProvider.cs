using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIProviderConnect.Providers;

/// <summary>
/// Concrete provider for decision models. Answers typed questions about application state with
/// probabilities. This is not a chat provider: <see cref="ChatAsync"/> throws
/// <see cref="AiErrorCodes.ChatNotSupported"/>. Model discovery is not implemented — a decisions
/// surface and a model catalog are different endpoints, and the core stays host-neutral.
/// </summary>
public sealed class DecisionProvider : AIProviderBase, IDecisionProvider
{
    private readonly string _decisionsEndpoint;
    private readonly string? _decisionsBaseUrl;

    public DecisionProvider(
        HttpClient httpClient,
        AIProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger? logger = null)
        : this(httpClient, options, catalog, providerId, logger ?? NullLogger.Instance, credentialResolver: null)
    {
    }

    public DecisionProvider(
        HttpClient httpClient,
        AIProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
        : base(httpClient, catalog, options, providerId, logger, credentialResolver)
    {
        var decisionOptions = options as DecisionProviderOptions
            ?? throw new ArgumentException(
                $"Options type '{options.GetType().Name}' is not DecisionProviderOptions. " +
                "DecisionProvider requires DecisionProviderOptions.",
                nameof(options));
        _decisionsEndpoint = decisionOptions.DecisionsEndpoint;
        _decisionsBaseUrl = decisionOptions.DecisionsBaseUrl;
    }

    /// <inheritdoc />
    public bool SupportsDecisions => true;

    /// <inheritdoc />
    public override bool SupportsModelDiscovery => false;

    /// <summary>
    /// A decision provider is not a chat provider. Always throws
    /// <see cref="AiException"/> with <see cref="AiErrorCodes.ChatNotSupported"/>.
    /// </summary>
    public override Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new AiException(
            AiErrorCodes.ChatNotSupported,
            $"Provider '{Id}' is a decision provider and does not support chat. Use DecideAsync.");

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

    // The decisions surface may live on a different origin than the provider BaseUrl. A full-URL
    // override in options wins; otherwise the effective base URL (per-request or configured) is used.
    private string ResolveDecisionsBaseUrl(RequestCredentials? credentials) =>
        !string.IsNullOrWhiteSpace(_decisionsBaseUrl)
            ? _decisionsBaseUrl!
            : EffectiveBaseUrl(Options, credentials);

    private Action<HttpRequestMessage> BuildHeaderConfigurator(RequestCredentials? credentials) =>
        request => SetBearerAuthentication(request, EffectiveApiKey(Options, credentials));
}
