using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Services;

using Microsoft.Extensions.DependencyInjection;

namespace AIProviderConnect.DependencyInjection;

/// <summary>
/// Fluent collector used by the <c>AddAiProviders(Action&lt;AIProviderRegistrationBuilder&gt;)</c>
/// overload to add or replace provider definitions, configure named options, and register
/// consumer-supplied <see cref="IAIProvider"/> implementations.
/// </summary>
public sealed class AIProviderRegistrationBuilder
{
    private readonly Dictionary<string, Func<IServiceProvider, IAIProvider>> _customProviders =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<ProviderDefinition> _definitions = [];

    private readonly HashSet<string> _manuallyRegisteredIds = new(StringComparer.OrdinalIgnoreCase);

    private readonly List<Action<IServiceCollection>> _optionRegistrations = [];

    private readonly List<(string ProviderId, Func<ProviderDefinition, ProviderDefinition> Transform)>
        _transforms = [];

    private readonly InMemoryModelOverrideStore _modelOverrides = new();

    private IModelOverrideStore? _modelOverrideStore;

    private bool _hasModelOverrides;

    private ICredentialResolver? _credentialResolver;

    /// <summary>
    /// Gets the ids registered with a custom <see cref="IAIProvider"/> factory. These are
    /// excluded from the automatic per-protocol registration loop.
    /// </summary>
    internal IReadOnlySet<string> ManuallyRegisteredIds => _manuallyRegisteredIds;

    /// <summary>
    /// Gets the consumer-supplied <see cref="IAIProvider"/> factories keyed by provider id.
    /// </summary>
    internal IReadOnlyDictionary<string, Func<IServiceProvider, IAIProvider>> CustomProviders =>
        _customProviders;

    /// <summary>
    /// Gets the deferred named-options registrations, applied after catalog seeding so
    /// consumer values win.
    /// </summary>
    internal IReadOnlyList<Action<IServiceCollection>> OptionRegistrations => _optionRegistrations;

    /// <summary>
    /// Gets the resolved model-override store: the consumer-supplied store when
    /// <see cref="UseModelOverrideStore"/> was called, otherwise the in-memory store populated by
    /// <see cref="OverrideModels"/>, or null when no overrides were registered.
    /// </summary>
    internal IModelOverrideStore? ModelOverrideStore =>
        _modelOverrideStore ?? (_hasModelOverrides ? _modelOverrides : null);

    /// <summary>
    /// Gets the consumer-supplied <see cref="ICredentialResolver"/>, or <c>null</c> when
    /// <see cref="UseCredentialResolver"/> was not called. No default implementation is registered.
    /// </summary>
    internal ICredentialResolver? CredentialResolver => _credentialResolver;

    /// <summary>
    /// Adds a new definition, or replaces an already-added definition with the same id
    /// (case-insensitive). Embedded definitions are overridden when the catalog is built.
    /// </summary>
    /// <param name="definition">The provider definition to add or replace.</param>
    public AIProviderRegistrationBuilder Add(ProviderDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        Upsert(_definitions, definition);
        return this;
    }

    /// <summary>
    /// Replaces an existing definition (embedded or previously added) in place via
    /// <c>def with { ... }</c>. Each transform is resolved against the definitions produced so
    /// far, so chained <see cref="Replace"/> calls on the same id compose.
    /// </summary>
    /// <param name="providerId">The id of the definition to replace.</param>
    /// <param name="transform">A function producing the replacement from the current definition.</param>
    public AIProviderRegistrationBuilder Replace(
        string providerId,
        Func<ProviderDefinition, ProviderDefinition> transform)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        ArgumentNullException.ThrowIfNull(transform);

        _transforms.Add((providerId, transform));
        return this;
    }

    /// <summary>
    /// Configures named options for a provider. Runs after default seeding, so it wins.
    /// </summary>
    /// <typeparam name="TOptions">The provider's options type.</typeparam>
    /// <param name="providerId">The provider id whose named options to configure.</param>
    /// <param name="configure">The configuration callback.</param>
    public AIProviderRegistrationBuilder Configure<TOptions>(
        string providerId,
        Action<TOptions> configure)
        where TOptions : AIProviderOptions
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        ArgumentNullException.ThrowIfNull(configure);

        _optionRegistrations.Add(
            services => services.AddOptions<TOptions>(providerId).Configure(configure));
        return this;
    }

    /// <summary>
    /// Registers a consumer-supplied <see cref="IAIProvider"/> for an id and excludes that id
    /// from the automatic per-protocol registration loop.
    /// </summary>
    /// <typeparam name="TProvider">The consumer's provider type.</typeparam>
    /// <param name="providerId">The provider id.</param>
    /// <param name="factory">A factory producing the provider from the service provider.</param>
    public AIProviderRegistrationBuilder AddProvider<TProvider>(
        string providerId,
        Func<IServiceProvider, TProvider> factory)
        where TProvider : class, IAIProvider
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        ArgumentNullException.ThrowIfNull(factory);

        _customProviders[providerId] = factory;
        _manuallyRegisteredIds.Add(providerId);
        return this;
    }

    /// <summary>
    /// Registers model/pricing overrides for a provider id. The overrides are merged transparently
    /// over that provider's <see cref="IModelDiscoveryProvider.GetModelsAsync"/> result at
    /// resolution time, so the consumer's call site is unchanged.
    /// </summary>
    /// <param name="providerId">The provider id the overrides apply to.</param>
    /// <param name="overrides">The overrides, applied in order.</param>
    public AIProviderRegistrationBuilder OverrideModels(
        string providerId,
        IEnumerable<ModelOverride> overrides)
    {
        ArgumentException.ThrowIfNullOrEmpty(providerId);
        ArgumentNullException.ThrowIfNull(overrides);

        _modelOverrides.Add(providerId, overrides);
        _hasModelOverrides = true;
        return this;
    }

    /// <summary>
    /// Replaces the default in-memory override store with a consumer-supplied source.
    /// </summary>
    /// <param name="store">The store backing model overrides.</param>
    public AIProviderRegistrationBuilder UseModelOverrideStore(IModelOverrideStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _modelOverrideStore = store;
        return this;
    }

    /// <summary>
    /// Registers a consumer-supplied <see cref="ICredentialResolver"/> consulted once per provider call
    /// to supply runtime credential/model overrides. Registered as a singleton; the library provides no
    /// default implementation.
    /// </summary>
    /// <param name="resolver">The resolver to consult per call.</param>
    public AIProviderRegistrationBuilder UseCredentialResolver(ICredentialResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _credentialResolver = resolver;
        return this;
    }

    /// <summary>
    /// Resolves the collected <see cref="Add"/> definitions and <see cref="Replace"/> transforms
    /// into the final set of consumer definitions to merge over the embedded catalog.
    /// </summary>
    internal IReadOnlyList<ProviderDefinition> BuildDefinitions(IProviderCatalog embedded)
    {
        if (_transforms.Count == 0)
            return _definitions;

        var merged = new List<ProviderDefinition>(_definitions);

        foreach (var (providerId, transform) in _transforms)
        {
            // Resolve against the running merged state first so chained Replace calls on the
            // same id compose, falling back to the embedded catalog for ids never Added.
            var baseDefinition =
                merged.FirstOrDefault(
                    d => string.Equals(d.Id, providerId, StringComparison.OrdinalIgnoreCase))
                ?? embedded.Get(providerId)
                ?? throw new ArgumentException(
                    $"Replace target provider '{providerId}' was not found in the catalog.",
                    nameof(providerId));

            Upsert(merged, transform(baseDefinition));
        }

        return merged;
    }

    private static void Upsert(List<ProviderDefinition> list, ProviderDefinition definition) =>
        ProviderCatalog.ProviderDefinitionListUpsert(list, definition);
}
