using System.Text.Json;

using AIProviderConnect.Constants;
using AIProviderConnect.Models;
using AIProviderConnect.Options;

namespace AIProviderConnect.Protocols;

public static class CatalogWireProtocol
{
    private const string ApiVersionKey = "apiVersion";
    private const string AcceptKey = "accept";
    private const string CatalogApiVersionHeader = "X-GitHub-Api-Version";
    private const string AcceptHeader = "Accept";

    /// <summary>
    /// Applies protocol-specific configuration from the options' ProtocolConfiguration
    /// dictionary to the typed options. Key names are defined and used only here.
    /// </summary>
    public static void ApplyProtocolConfiguration(AIProviderOptions options)
    {
        if (options.ProtocolConfiguration is null) return;

        if (options.ProtocolConfiguration.TryGetValue(ApiVersionKey, out var version)
            && !string.IsNullOrWhiteSpace(version))
        {
            options.DefaultHeaders[CatalogApiVersionHeader] = version;
        }

        if (options.ProtocolConfiguration.TryGetValue(AcceptKey, out var accept)
            && !string.IsNullOrWhiteSpace(accept))
        {
            options.DefaultHeaders[AcceptHeader] = accept;
        }
    }

    public static IReadOnlyList<AIModel> ParseModels(JsonElement json, string providerId) =>
        ProtocolParsingHelpers.ParseModelArray(
            json, rootProperty: null, providerId,
            x => new AIModel
            {
                Id = ProtocolParsingHelpers.SafeGetString(x, CatalogPropertyNames.Id),
                ProviderId = providerId,
                DisplayName = ProtocolParsingHelpers.SafeGetString(x, CatalogPropertyNames.Name),
                OwnedBy = x.TryGetProperty(CatalogPropertyNames.Publisher, out var publisher)
                              ? publisher.GetString()
                              : null
            });
}
