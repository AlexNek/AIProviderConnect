using System.Text.Json;

namespace AIProviderConnect.Models;

public static class ProviderProtocolMapper
{
    private static readonly Dictionary<string, EProviderProtocol> ByJsonValue =
        new(StringComparer.OrdinalIgnoreCase)
            {
                ["openaicompatible"] = EProviderProtocol.OpenAICompatible,
                ["anthropiccompatible"] = EProviderProtocol.MessagesApi,
                ["geminicompatible"] = EProviderProtocol.KeyQuery,
                ["githubmodelscompatible"] = EProviderProtocol.Catalog,
                ["hybridgateway"] = EProviderProtocol.HybridGateway,
                ["native"] = EProviderProtocol.Native,
            };

    private static readonly Dictionary<EProviderProtocol, string> JsonByEnum =
        ByJsonValue.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public static EProviderProtocol FromJson(string jsonProtocol)
    {
        if (ByJsonValue.TryGetValue(jsonProtocol, out var protocol))
        {
            return protocol;
        }

        throw new JsonException(
            $"Unknown protocol '{jsonProtocol}'. Known values: {string.Join(", ", ByJsonValue.Keys)}.");
    }

    public static string ToJson(EProviderProtocol protocol)
    {
        if (JsonByEnum.TryGetValue(protocol, out var json))
        {
            return json;
        }

        throw new ArgumentOutOfRangeException(nameof(protocol), protocol, "Unsupported protocol value.");
    }
}
