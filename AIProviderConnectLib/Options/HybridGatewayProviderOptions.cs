using AIProviderConnect.Constants;

namespace AIProviderConnect.Options;

/// <summary>
/// Configuration options for hybrid gateway providers.
/// </summary>
public sealed class HybridGatewayProviderOptions : AIProviderOptions, IChatAndModelsEndpointOptions
{
    /// <summary>
    /// Gets or sets the endpoint path for chat completions.
    /// </summary>
    public string ChatEndpoint { get; set; } = EndpointDefaults.ChatCompletions;

    /// <summary>
    /// Gets or sets the endpoint path for listing models.
    /// </summary>
    public string ModelsEndpoint { get; set; } = EndpointDefaults.Models;
}
