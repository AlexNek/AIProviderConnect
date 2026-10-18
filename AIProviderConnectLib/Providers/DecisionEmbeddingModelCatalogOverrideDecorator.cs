using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Services;

namespace AIProviderConnect.Providers;

/// <summary>
/// Transparent decorator for a non-streaming provider that answers decisions and also embeds —
/// e.g. an authored transport deriving <see cref="AIProviderBase"/> with an embeddings surface.
/// Merges consumer-supplied <see cref="ModelOverride"/> entries over the inner provider's model list
/// while forwarding both capabilities, so <c>is IDecisionProvider</c> and <c>is IEmbeddingProvider</c>
/// stay accurate on the decorated id. Selected instead of
/// <see cref="DecisionModelCatalogOverrideDecorator"/> when the inner provider implements
/// <see cref="IEmbeddingProvider"/>. (Selection happens in
/// <c>AIProviderServiceCollectionExtensions.ApplyModelOverrides</c>.)
/// </summary>
public sealed class DecisionEmbeddingModelCatalogOverrideDecorator
    : IAIProvider, IModelDiscoveryProvider, IDecisionProvider, IEmbeddingProvider
{
    private readonly IAIProvider _inner;
    private readonly IDecisionProvider _innerDecision;
    private readonly IModelOverrideStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecisionEmbeddingModelCatalogOverrideDecorator"/> class.
    /// </summary>
    /// <param name="inner">The provider being decorated; must implement <see cref="IDecisionProvider"/>.</param>
    /// <param name="store">The consumer-supplied override source.</param>
    public DecisionEmbeddingModelCatalogOverrideDecorator(IAIProvider inner, IModelOverrideStore store)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _innerDecision = inner as IDecisionProvider
            ?? throw new ArgumentException(
                "Inner provider must implement IDecisionProvider.", nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public string Id => _inner.Id;

    /// <inheritdoc />
    public bool IsEnabled => _inner.IsEnabled;

    /// <inheritdoc />
    public EProviderProtocol Protocol => _inner.Protocol;

    /// <inheritdoc />
    public bool RequiresApiKey => _inner.RequiresApiKey;

    /// <inheritdoc />
    public bool SupportsDecisions => _innerDecision.SupportsDecisions;

    /// <inheritdoc />
    public EDecisionSource DecisionSource => _innerDecision.DecisionSource;

    /// <summary>
    /// True when the inner provider supports live model discovery or consumer-supplied overrides
    /// are available.
    /// </summary>
    public bool SupportsModelDiscovery =>
        (_inner is IModelDiscoveryProvider discovery && discovery.SupportsModelDiscovery)
        || _store.Get(_inner.Id).Count > 0;

    /// <inheritdoc />
    public Task<ChatCompletionResponse> ChatAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        _inner.ChatAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<DecisionResponse> DecideAsync(
        DecisionRequest request,
        CancellationToken cancellationToken = default) =>
        _innerDecision.DecideAsync(request, cancellationToken);

    /// <inheritdoc />
    public Task<EmbeddingResponse> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default) =>
        _inner is IEmbeddingProvider embedding
            ? embedding.EmbedAsync(request, cancellationToken)
            : throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{_inner.Id}' does not support embeddings.");

    /// <inheritdoc />
    public async Task<IReadOnlyList<AIModel>> GetModelsAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<AIModel> live = [];

        if (_inner is IModelDiscoveryProvider discovery)
        {
            try
            {
                live = await discovery.GetModelsAsync(cancellationToken);
            }
            catch (AiException ex) when (ex.Code == AiErrorCodes.ModelDiscoveryNotSupported)
            {
                // No live API — the base list is empty and only the consumer's overrides apply.
                live = [];
            }
        }

        return ModelOverrideMerger.Merge(live, _store.Get(_inner.Id), _inner.Id);
    }
}
