using System.Text.Json.Serialization;

using AIProviderConnect.Models;

namespace AIProviderConnect.Models;

/// <summary>
/// Per-operation endpoint override carried by <see cref="ProviderDefinition.Endpoints"/>.
/// All members are optional: <see cref="Path"/> is a relative path resolved against the
/// operation's effective base URL (the <see cref="BaseUrl"/> override when present,
/// otherwise the definition's common <c>baseUrl</c>); <see cref="BaseUrl"/> is used only
/// when the surface does not sit under the definition's common base; <see cref="Protocol"/>
/// overrides the operation's wire family.
/// </summary>
public sealed record EndpointDefinition
{
    /// <summary>
    /// Gets the operation's base-URL override. Used only when the operation's surface
    /// lives on a different root than the definition's common <c>baseUrl</c>.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Gets the operation's endpoint path, resolved against the operation's effective
    /// base URL. When null the operation keeps its inherited path.
    /// </summary>
    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }

    /// <summary>
    /// Gets the operation's wire family. When null the definition's default protocol applies.
    /// Deserialized from JSON string via <see cref="EProviderProtocolNullableJsonConverter"/>.
    /// </summary>
    [JsonPropertyName("protocol")]
    [JsonConverter(typeof(EProviderProtocolNullableJsonConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EProviderProtocol? Protocol { get; init; }
}
