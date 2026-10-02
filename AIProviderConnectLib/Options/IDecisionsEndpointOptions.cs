namespace AIProviderConnect.Options;

/// <summary>
/// Internal contract for options classes that carry a decisions endpoint and its
/// base-URL override. Allows providers to access decisions configuration without
/// duplicating switch arms, mirroring <see cref="IEmbeddingsEndpointOptions"/>.
/// </summary>
internal interface IDecisionsEndpointOptions
{
    /// <summary>
    /// Gets or sets the endpoint path for decision calls.
    /// </summary>
    string DecisionsEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the base-URL override for the decisions surface; used only when
    /// the decisions surface lives on a different root than the provider base URL.
    /// </summary>
    string? DecisionsBaseUrl { get; set; }
}
