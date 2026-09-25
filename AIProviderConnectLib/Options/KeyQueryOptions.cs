using AIProviderConnect.Constants;

namespace AIProviderConnect.Options;

/// <summary>
/// Configuration options for key-query (key-in-URL) compatible providers.
/// Endpoint patterns use <c>{model}</c> as the model-name placeholder.
/// </summary>
public sealed class KeyQueryOptions : AIProviderOptions
{
    /// <summary>
    /// Gets or sets the format pattern for the generate-content endpoint.
    /// The pattern must contain a <c>{model}</c> placeholder for the model name.
    /// </summary>
    public string ChatEndpoint { get; set; } = EndpointDefaults.KeyQuery.GenerateContent;

    /// <summary>
    /// Gets or sets the format pattern for the streaming generate-content endpoint.
    /// The pattern must contain a <c>{model}</c> placeholder for the model name.
    /// </summary>
    public string StreamEndpoint { get; set; } = EndpointDefaults.KeyQuery.StreamGenerateContent;

    /// <summary>
    /// Gets or sets the endpoint path for listing models. Default is "models".
    /// </summary>
    public string ModelsEndpoint { get; set; } = EndpointDefaults.Models;
}
