namespace AIProviderConnect.Options;

/// <summary>
/// Internal contract for options classes that carry an embeddings endpoint.
/// Allows providers to access embeddings configuration without duplicating switch arms.
/// </summary>
internal interface IEmbeddingsEndpointOptions
{
    /// <summary>
    /// Gets or sets the endpoint path for embeddings.
    /// </summary>
    string EmbeddingsEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the default embedding model identifier.
    /// </summary>
    string DefaultEmbeddingModel { get; set; }

    /// <summary>
    /// Gets or sets the base-URL override for the embeddings surface; used only when
    /// the embeddings surface lives on a different root than the provider base URL.
    /// </summary>
    string? EmbeddingsBaseUrl { get; set; }
}
