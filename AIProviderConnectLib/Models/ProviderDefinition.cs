using System.Text.Json.Serialization;

using AIProviderConnect.Constants;

namespace AIProviderConnect.Models;

/// <summary>
/// Immutable provider identity and default runtime configuration, loaded from embedded JSON.
/// </summary>
public sealed record ProviderDefinition
{
    /// <summary>
    /// Gets the base URL of the provider's API.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public required string BaseUrl { get; init; }

    /// <summary>
    /// Gets the category of the provider.
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>
    /// Gets the endpoint path for chat completions.
    /// </summary>
    [JsonPropertyName("chatEndpoint")]
    public string ChatEndpoint { get; init; } = EndpointDefaults.ChatCompletions;

    /// <summary>
    /// Gets the display name of the provider.
    /// </summary>
    [JsonPropertyName("displayName")]
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets whether the provider has a model discovery API.
    /// </summary>
    [JsonPropertyName("hasModelDiscoveryApi")]
    public bool HasModelDiscoveryApi { get; init; }

    /// <summary>
    /// Gets the unique identifier of the provider.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Gets the endpoint path for the Messages API (Anthropic wire).
    /// Defaults to <see cref="EndpointDefaults.Messages"/>; only used by MessagesApi-protocol providers.
    /// </summary>
    [JsonPropertyName("messagesEndpoint")]
    public string MessagesEndpoint { get; init; } = EndpointDefaults.Messages;

    /// <summary>
    /// Gets the endpoint path for listing models.
    /// </summary>
    [JsonPropertyName("modelsEndpoint")]
    public string ModelsEndpoint { get; init; } = EndpointDefaults.Models;

    /// <summary>
    /// Gets the wire protocol used by this provider.
    /// Deserialized from JSON string via <see cref="EProviderProtocolJsonConverter"/>.
    /// </summary>
    [JsonPropertyName("protocol")]
    [JsonConverter(typeof(EProviderProtocolJsonConverter))]
    public required EProviderProtocol Protocol { get; init; }

    /// <summary>
    /// Gets protocol-specific configuration key-value pairs.
    /// Keys and values are defined by each protocol implementation; the common model
    /// carries no protocol-specific knowledge.
    /// </summary>
    [JsonPropertyName("protocolConfiguration")]
    public IReadOnlyDictionary<string, string>? ProtocolConfiguration { get; init; }
}
