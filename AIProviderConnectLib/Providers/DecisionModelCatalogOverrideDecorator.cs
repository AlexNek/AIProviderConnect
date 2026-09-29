using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Services;

namespace AIProviderConnect.Providers;

/// <summary>
/// Transparent decorator for a decision-capable provider. Merges consumer-supplied
/// <see cref="ModelOverride"/> entries over the inner provider's model list while forwarding the
/// <see cref="IDecisionProvider"/> capability, so <c>is IDecisionProvider</c> on a decorated
/// <c>Decision</c> provider id stays accurate. Used instead of
/// <see cref="NonStreamingModelCatalogOverrideDecorator"/> when the inner provider supports decisions.
/// </summary>
public sealed class DecisionModelCatalogOverrideDecorator
    : IAIProvider, IModelDiscoveryProvider, IDecisionProvider
{
    private readonly IAIProvider _inner;
    private readonly IDecisionProvider _innerDecision;
    private readonly IModelOverrideStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="DecisionModelCatalogOverrideDecorator"/> class.
    /// </summary>
    /// <param name="inner">The provider being decorated; must implement <see cref="IDecisionProvider"/>.</param>
    /// <param name="store">The consumer-supplied override source.</param>
    public DecisionModelCatalogOverrideDecorator(IAIProvider inner, IModelOverrideStore store)
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
    public bool SupportsDecisions => _innerDecision.SupportsDecisions;

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
