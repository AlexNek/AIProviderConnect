using AIProviderConnect.Abstractions;

namespace ScraperTool.Services;

/// <summary>
/// Extends <see cref="IAIProviderFactory"/> with the ability to create a provider using
/// a transient (possibly unsaved) API key, used for connection testing before the key is
/// committed to settings.
/// </summary>
public interface ITransientCredentialProviderFactory : IAIProviderFactory
{
    /// <summary>
    /// Creates a provider for an explicit (possibly unsaved) API key, used to test a
    /// connection before the key is committed to settings. Does not mutate shared state.
    /// </summary>
    IAIProvider GetProvider(string providerId, string apiKey);
}
