using AIProviderConnect.Abstractions;
using AIProviderConnect.Options;

using Microsoft.Extensions.Logging;

namespace AIProviderConnect.Providers;

/// <summary>
/// Concrete provider for OpenAI-compatible and hybrid gateway AI providers.
/// Metadata (Name, DisplayName, etc.) is resolved from the ProviderCatalog at runtime.
/// </summary>
public sealed class OpenAICompatibleProvider : OpenAICompatibleProviderBase, IEmbeddingProvider
{
    public OpenAICompatibleProvider(
        HttpClient httpClient,
        AIProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger? logger = null)
        : base(httpClient, catalog, options, providerId, logger)
    {
    }

    public OpenAICompatibleProvider(
        HttpClient httpClient,
        AIProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
        : base(httpClient, catalog, options, providerId, logger, credentialResolver)
    {
    }
}
