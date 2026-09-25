using AIProviderConnect.Abstractions;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;
using AIProviderConnect.Services;

namespace AIProviderConnect.Providers;

/// <summary>
/// Transparent decorator that merges consumer-supplied <see cref="ModelOverride"/> entries over the
/// inner provider's <see cref="IModelDiscoveryProvider.GetModelsAsync"/> result. Every other
/// capability (<see cref="IAIProvider"/> and streaming) is delegated to the inner provider
/// unchanged. Registered only for provider ids that have overrides, so undecorated providers
/// behave exactly as before.
/// </summary>
public sealed class ModelCatalogOverrideDecorator
    : IAIProvider, IModelDiscoveryProvider, IStreamingChatProvider
{
    private readonly IAIProvider _inner;
    private readonly IModelOverrideStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelCatalogOverrideDecorator"/> class.
    /// </summary>
    /// <param name="inner">The provider being decorated.</param>
    /// <param name="store">The consumer-supplied override source.</param>
    public ModelCatalogOverrideDecorator(IAIProvider inner, IModelOverrideStore store)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public string Id => _inner.Id;

    /// <inheritdoc />
    public bool IsEnabled => _inner.IsEnabled;

    /// <inheritdoc />
    public EProviderProtocol Protocol => _inner.Protocol;

    /// <summary>
    /// True when the inner provider supports live model discovery or consumer-supplied overrides
    /// are available. "true" means either the inner provider has a live discovery API or the
    /// consumer has registered override entries that can be returned as a model list.
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

    /// <inheritdoc />
    public IAsyncEnumerable<StreamingChatChunk> StreamAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default) =>
        _inner is IStreamingChatProvider streaming
            ? streaming.StreamAsync(request, cancellationToken)
            : throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{_inner.Id}' does not support streaming.");
}
