using AIProviderConnect.Constants;

namespace AIProviderConnect.Options;

/// <summary>
/// Configuration options for decision providers.
/// </summary>
public sealed class DecisionProviderOptions : AIProviderOptions, IDecisionsEndpointOptions
{
    /// <summary>
    /// Gets or sets the endpoint path for decision calls, resolved against the provider
    /// <see cref="AIProviderOptions.BaseUrl"/>. Default is <see cref="EndpointDefaults.Decisions"/>.
    /// </summary>
    public string DecisionsEndpoint { get; set; } = EndpointDefaults.Decisions;

    /// <summary>
    /// Gets or sets a full-URL override for the decisions surface, for hosts whose decisions endpoint
    /// lives on a different origin than the provider <see cref="AIProviderOptions.BaseUrl"/>.
    /// When null, the decisions endpoint is resolved against the provider base URL.
    /// </summary>
    public string? DecisionsBaseUrl { get; set; }
}
