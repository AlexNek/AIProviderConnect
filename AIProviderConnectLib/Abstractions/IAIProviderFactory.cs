using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Factory for creating and managing AI providers.
/// </summary>
public interface IAIProviderFactory
{
    /// <summary>
    /// Gets a provider by its unique identifier.
    /// </summary>
    /// <param name="providerId">The provider ID (e.g. "openai", "anthropic").</param>
    /// <returns>The provider instance.</returns>
    IAIProvider GetProvider(string providerId);

    /// <summary>
    /// Gets a transient provider instance bound to the supplied per-call <paramref name="overrides"/>.
    /// The overrides take priority over any <see cref="ICredentialResolver"/> and over the configured
    /// options, and apply to a freshly constructed provider — the shared singleton is left unchanged.
    /// </summary>
    /// <param name="providerId">The provider ID (e.g. "openai", "anthropic").</param>
    /// <param name="overrides">The runtime credential/model overrides for this provider instance.</param>
    /// <returns>A transient provider instance configured with <paramref name="overrides"/>.</returns>
    IAIProvider GetProvider(string providerId, RequestCredentials overrides);
}
