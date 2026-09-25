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
}
