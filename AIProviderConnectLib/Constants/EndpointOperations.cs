using AIProviderConnect.Models;

namespace AIProviderConnect.Constants;

/// <summary>
/// The fixed operation-key vocabulary of <see cref="ProviderDefinition.Endpoints"/>,
/// mirroring <see cref="EndpointDefaults"/>, plus the single lookup helper every
/// consumer uses instead of indexing the dictionary at a call site.
/// </summary>
public static class EndpointOperations
{
    public const string Chat = "chat";

    public const string Decisions = "decisions";

    public const string Embeddings = "embeddings";

    public const string Messages = "messages";

    public const string Models = "models";

    /// <summary>
    /// Resolves <paramref name="operation"/> in <paramref name="endpoints"/>, matching
    /// ordinally ignore-case; returns null when the map is null or the key is absent.
    /// </summary>
    public static EndpointDefinition? Find(
        IReadOnlyDictionary<string, EndpointDefinition>? endpoints,
        string operation)
    {
        if (endpoints is null)
        {
            return null;
        }

        foreach (var key in endpoints.Keys)
        {
            if (string.Equals(key, operation, StringComparison.OrdinalIgnoreCase))
            {
                return endpoints[key];
            }
        }

        return null;
    }
}
