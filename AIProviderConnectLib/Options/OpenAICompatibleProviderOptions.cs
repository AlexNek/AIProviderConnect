using AIProviderConnect.Constants;

namespace AIProviderConnect.Options;

/// <summary>
/// Configuration options for OpenAI-compatible providers.
/// </summary>
public sealed class OpenAICompatibleProviderOptions : AIProviderOptions, IChatAndModelsEndpointOptions
{
    /// <summary>
    /// Gets or sets the endpoint path for chat completions. Default is "chat/completions".
    /// </summary>
    public string ChatEndpoint { get; set; } = EndpointDefaults.ChatCompletions;

    /// <summary>
    /// Gets or sets the endpoint path for listing models. Default is "models".
    /// </summary>
    public string ModelsEndpoint { get; set; } = EndpointDefaults.Models;
}
