using System.Text.Json;

using AIProviderConnect.Abstractions;
using AIProviderConnect.Models;
using AIProviderConnect.Options;
using AIProviderConnect.Protocols;

using Microsoft.Extensions.Logging;

namespace AIProviderConnect.Providers;

/// <summary>
/// Provider for model catalog services that expose a discoverable catalog of models
/// with protocol-driven headers applied via <see cref="CatalogWireProtocol"/>.
/// </summary>
public sealed class ModelCatalogProvider : OpenAICompatibleProviderBase
{
    public ModelCatalogProvider(
        HttpClient httpClient,
        OpenAICompatibleProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger? logger = null)
        : base(httpClient, catalog, options, providerId, logger)
    {
    }

    public ModelCatalogProvider(
        HttpClient httpClient,
        OpenAICompatibleProviderOptions options,
        IProviderCatalog catalog,
        string providerId,
        ILogger logger,
        ICredentialResolver? credentialResolver)
        : base(httpClient, catalog, options, providerId, logger, credentialResolver)
    {
    }

    protected override IReadOnlyList<AIModel> ParseModels(JsonElement json) =>
        CatalogWireProtocol.ParseModels(json, Id, Options.ProtocolConfiguration);
}
