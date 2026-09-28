using AIProviderConnect.Abstractions;
using AIProviderConnect.DependencyInjection;
using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;

namespace AIProviderConnect.Providers;

/// <summary>
/// Builds a transient provider instance bound to caller-supplied <see cref="RequestCredentials"/>, so
/// <c>GetProvider(id, overrides)</c> can attach fixed credentials to a freshly constructed provider
/// without a decorator around the shared singleton (which could never change the inner provider's
/// outgoing auth header or base URL). The activator captures only the raw construction closure — fresh
/// named options, catalog, logger, resolver — and nothing else. Credential attachment happens on the
/// concrete provider <em>before</em> the model-override decorator wraps it.
/// </summary>
internal sealed class ProviderActivator
{
    private readonly Func<IServiceProvider, IAIProvider> _construction;
    private readonly string _providerId;
    private readonly IServiceProvider _services;

    internal ProviderActivator(
        IServiceProvider services,
        Func<IServiceProvider, IAIProvider> construction,
        string providerId)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _construction = construction ?? throw new ArgumentNullException(nameof(construction));
        _providerId = providerId;
    }

    /// <summary>
    /// Constructs a fresh provider, attaches <paramref name="overrides"/> as its fixed credentials, then
    /// applies model-override decoration exactly as the singleton path would. When the constructed
    /// provider is not an <see cref="AIProviderBase"/> the override cannot be applied and the call throws
    /// rather than silently handing back a provider that would ignore the caller's credentials.
    /// </summary>
    public IAIProvider Create(RequestCredentials overrides)
    {
        ArgumentNullException.ThrowIfNull(overrides);

        var provider = _construction(_services);
        if (provider is not AIProviderBase baseProvider)
            throw new AiException(
                AiErrorCodes.ConfigurationError,
                $"Provider '{_providerId}' does not derive from AIProviderBase, so per-request credentials cannot be applied.");

        baseProvider.FixedCredentials = overrides;
        return AIProviderServiceCollectionExtensions.ApplyModelOverrides(_services, _providerId, provider);
    }
}
