using AIProviderConnect.Constants;

namespace AIProviderConnect.Options;

/// <summary>
/// Configuration options for Messages API-compatible providers.
/// </summary>
public sealed class MessagesApiOptions : AIProviderOptions
{
    /// <summary>
    /// Gets or sets the endpoint path for messages. Default is "messages".
    /// </summary>
    public string MessagesEndpoint { get; set; } = EndpointDefaults.Messages;

    /// <summary>
    /// Gets or sets the endpoint path for listing models. Default is "models".
    /// </summary>
    public string ModelsEndpoint { get; set; } = EndpointDefaults.Models;
}
