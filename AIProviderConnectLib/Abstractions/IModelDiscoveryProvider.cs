using AIProviderConnect.Exceptions;
using AIProviderConnect.Models;

namespace AIProviderConnect.Abstractions;

/// <summary>
/// Provider implementation that knows how to discover models.
/// The <see cref="SupportsModelDiscovery"/> property tells callers whether
/// the currently configured provider instance supports this operation.
/// </summary>
public interface IModelDiscoveryProvider
{
    /// <summary>
    /// Gets whether the currently configured provider instance supports model discovery.
    /// Check this before calling <see cref="GetModelsAsync"/>.
    /// </summary>
    bool SupportsModelDiscovery { get; }

    /// <summary>
    /// Discovers models available from the provider.
    /// Throws <see cref="AiException"/> with <see cref="AiErrorCodes.ModelDiscoveryNotSupported"/>
    /// if <see cref="SupportsModelDiscovery"/> is false.
    /// </summary>
    Task<IReadOnlyList<AIModel>> GetModelsAsync(CancellationToken cancellationToken = default);
}
